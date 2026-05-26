using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using ARQFlow.Modules.Documentacao.Views;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ARQFlow.Modules.Documentacao.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class CotarExternaCommand : IExternalCommand
    {
        // ═════════════════════════════════════════════════════════════════════
        // MODELOS DE DADOS
        // ═════════════════════════════════════════════════════════════════════

        public enum CotarModoExterna { TodosVisiveis, SelecionarManual }

        public class CotarExternaParams
        {
            public ElementId DimensionTypeId { get; set; }
            public CotarModoExterna Modo { get; set; } = CotarModoExterna.TodosVisiveis;
            public double DistanciaCm { get; set; } = 100.0;
            public bool CotaHorizontal { get; set; } = true;
            public bool CotaVertical { get; set; } = true;
        }

        // ═════════════════════════════════════════════════════════════════════
        // IEXTERNALCOMMAND
        // ═════════════════════════════════════════════════════════════════════

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            if (view.ViewType != ViewType.FloorPlan && view.ViewType != ViewType.CeilingPlan)
            {
                TaskDialog.Show("Erro", "Este comando só funciona em Planta Baixa ou Planta de Forro.");
                return Result.Failed;
            }

            var ui = new CotarExternaView(doc);
            if (ui.ShowDialog() != true) return Result.Cancelled;

            CotarExternaParams p = ui.Parametros;

            List<Wall> paredes;
            if (p.Modo == CotarModoExterna.SelecionarManual)
            {
                paredes = SelecionarParedes(uidoc, doc);
                if (paredes == null || !paredes.Any()) return Result.Cancelled;
            }
            else
            {
                paredes = GetParedesVisiveis(doc, view);
                if (!paredes.Any())
                {
                    TaskDialog.Show("Cotar Externa", "Nenhuma parede encontrada na vista atual.");
                    return Result.Succeeded;
                }
            }

            DimensionType dimType = doc.GetElement(p.DimensionTypeId) as DimensionType;
            int criadas = 0;

            using (Transaction tx = new Transaction(doc, "Cotar Extensão Geral"))
            {
                tx.Start();
                criadas = CriarCotasGerais(doc, view, paredes, p, dimType);
                tx.Commit();
            }

            TaskDialog.Show("Cotar Externa",
                criadas > 0
                    ? $"✔ {criadas} cota(s) geral(is) inserida(s) com sucesso."
                    : "Nenhuma cota pôde ser criada.\nCertifique-se de que as paredes do projeto estão alinhadas com os eixos X/Y da vista.");

            return Result.Succeeded;
        }

        // ═════════════════════════════════════════════════════════════════════
        // COLETA DE PAREDES
        // ═════════════════════════════════════════════════════════════════════

        private List<Wall> SelecionarParedes(UIDocument uidoc, Document doc)
        {
            try
            {
                IList<Reference> refs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new WallSelectionFilter(),
                    "Selecione as paredes a incluir na cota geral (Concluir para confirmar)");
                return refs.Select(r => doc.GetElement(r.ElementId) as Wall).ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return null; }
        }

        private List<Wall> GetParedesVisiveis(Document doc, View view)
        {
            ElementId nivelVista = (view as ViewPlan)?.GenLevel?.Id;
            return new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .Where(w =>
                    w.WallType?.Kind != WallKind.Curtain &&
                    w.Location is LocationCurve lc &&
                    lc.Curve is Line &&
                    (nivelVista == null || w.LevelId == nivelVista))
                .ToList();
        }

        // ═════════════════════════════════════════════════════════════════════
        // MOTOR PRINCIPAL
        // ═════════════════════════════════════════════════════════════════════

        private int CriarCotasGerais(
            Document doc, View view,
            List<Wall> paredes,
            CotarExternaParams p,
            DimensionType dimType)
        {
            // No modo manual o usuário já definiu o grupo; no automático usa o maior grupo conectado.
            List<Wall> grupo;
            if (p.Modo == CotarModoExterna.SelecionarManual)
            {
                grupo = paredes;
            }
            else
            {
                var grupos = AgruparParedesConectadas(paredes);
                grupo = grupos.OrderByDescending(g => g.Count).First();
            }

            // Coleta faces cujas normais são paralelas ao eixo X (paredes N-S → medem largura)
            // e faces paralelas ao eixo Y (paredes E-O → medem altura).
            var facesX = new List<(double PosX, Reference Ref)>();
            var facesY = new List<(double PosY, Reference Ref)>();

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            double z    = (view as ViewPlan)?.GenLevel?.Elevation ?? 0;

            foreach (Wall w in grupo)
            {
                BoundingBoxXYZ bb = w.get_BoundingBox(view);
                if (bb != null)
                {
                    minX = Math.Min(minX, bb.Min.X); maxX = Math.Max(maxX, bb.Max.X);
                    minY = Math.Min(minY, bb.Min.Y); maxY = Math.Max(maxY, bb.Max.Y);
                }

                try
                {
                    var sideFaces = HostObjectUtils.GetSideFaces(w, ShellLayerType.Exterior)
                        .Concat(HostObjectUtils.GetSideFaces(w, ShellLayerType.Interior));

                    foreach (Reference r in sideFaces)
                    {
                        if (w.GetGeometryObjectFromReference(r) is PlanarFace pf)
                        {
                            XYZ n = pf.FaceNormal;
                            if (Math.Abs(n.X) > 0.9 && Math.Abs(n.Y) < 0.2)
                                facesX.Add((pf.Origin.X, r));
                            else if (Math.Abs(n.Y) > 0.9 && Math.Abs(n.X) < 0.2)
                                facesY.Add((pf.Origin.Y, r));
                        }
                    }
                }
                catch { }
            }

            if (minX == double.MaxValue || minY == double.MaxValue) return 0;

            double dist = UnitUtils.ConvertToInternalUnits(p.DistanciaCm, UnitTypeId.Centimeters);
            double ext  = UnitUtils.ConvertToInternalUnits(60, UnitTypeId.Centimeters);
            int criadas = 0;

            // Cota horizontal: linha abaixo do projeto, mede a largura E-O
            if (p.CotaHorizontal && facesX.Count >= 2)
            {
                Reference refEsq = facesX.OrderBy(f => f.PosX).First().Ref;
                Reference refDir = facesX.OrderBy(f => f.PosX).Last().Ref;

                Line dimLine = Line.CreateBound(
                    new XYZ(minX - ext, minY - dist, z),
                    new XYZ(maxX + ext, minY - dist, z));

                if (CriarDimensao(doc, view, dimLine, refEsq, refDir, dimType)) criadas++;
            }

            // Cota vertical: linha à esquerda do projeto, mede a altura N-S
            if (p.CotaVertical && facesY.Count >= 2)
            {
                Reference refInf = facesY.OrderBy(f => f.PosY).First().Ref;
                Reference refSup = facesY.OrderBy(f => f.PosY).Last().Ref;

                Line dimLine = Line.CreateBound(
                    new XYZ(minX - dist, minY - ext, z),
                    new XYZ(minX - dist, maxY + ext, z));

                if (CriarDimensao(doc, view, dimLine, refInf, refSup, dimType)) criadas++;
            }

            return criadas;
        }

        private bool CriarDimensao(
            Document doc, View view,
            Line dimLine, Reference ref1, Reference ref2,
            DimensionType dimType)
        {
            using (SubTransaction st = new SubTransaction(doc))
            {
                st.Start();
                try
                {
                    ReferenceArray refs = new ReferenceArray();
                    refs.Append(ref1);
                    refs.Append(ref2);
                    doc.Create.NewDimension(view, dimLine, refs, dimType);
                    st.Commit();
                    return true;
                }
                catch { st.RollBack(); return false; }
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // AGRUPAMENTO POR CONECTIVIDADE (Union-Find)
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Agrupa paredes que formam um conjunto conectado — paredes cujas extremidades
        /// (ou pontos de junção T) estão a menos de 5 cm umas das outras.
        /// Isso evita que paredes isoladas e distantes do projeto principal sejam incluídas
        /// na medição de extensão geral.
        /// </summary>
        private static List<List<Wall>> AgruparParedesConectadas(List<Wall> paredes)
        {
            int n = paredes.Count;
            if (n == 0) return new List<List<Wall>>();

            double tol = UnitUtils.ConvertToInternalUnits(5.0, UnitTypeId.Centimeters);
            int[] parent = Enumerable.Range(0, n).ToArray();

            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    if (Find(parent, i) != Find(parent, j) && SaoConectadas(paredes[i], paredes[j], tol))
                        Union(parent, i, j);

            return paredes
                .Select((w, i) => (w, Root: Find(parent, i)))
                .GroupBy(x => x.Root)
                .Select(g => g.Select(x => x.w).ToList())
                .ToList();
        }

        private static bool SaoConectadas(Wall w1, Wall w2, double tol)
        {
            var seg1 = (Line)((LocationCurve)w1.Location).Curve;
            var seg2 = (Line)((LocationCurve)w2.Location).Curve;

            XYZ[] pts1 = { seg1.GetEndPoint(0), seg1.GetEndPoint(1) };
            XYZ[] pts2 = { seg2.GetEndPoint(0), seg2.GetEndPoint(1) };

            // Extremidade-a-extremidade (junção de canto)
            foreach (var p1 in pts1)
                foreach (var p2 in pts2)
                    if (Dist2D(p1, p2) <= tol) return true;

            // Extremidade de w1 sobre o corpo de w2 (junção T)
            foreach (var p1 in pts1)
                if (DistToSeg2D(p1, seg2) <= tol) return true;

            // Extremidade de w2 sobre o corpo de w1 (junção T)
            foreach (var p2 in pts2)
                if (DistToSeg2D(p2, seg1) <= tol) return true;

            return false;
        }

        private static double Dist2D(XYZ a, XYZ b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // Distância de um ponto a um segmento de reta, projetada no plano XY.
        private static double DistToSeg2D(XYZ pt, Line seg)
        {
            XYZ p0 = seg.GetEndPoint(0), p1 = seg.GetEndPoint(1);
            double dx = p1.X - p0.X, dy = p1.Y - p0.Y;
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-12) return Dist2D(pt, p0);

            double t = Math.Max(0, Math.Min(1,
                ((pt.X - p0.X) * dx + (pt.Y - p0.Y) * dy) / len2));

            double px = p0.X + t * dx - pt.X;
            double py = p0.Y + t * dy - pt.Y;
            return Math.Sqrt(px * px + py * py);
        }

        private static int Find(int[] p, int i)
        {
            while (p[i] != i) { p[i] = p[p[i]]; i = p[i]; }
            return i;
        }

        private static void Union(int[] p, int i, int j)
        {
            int ri = Find(p, i), rj = Find(p, j);
            if (ri != rj) p[ri] = rj;
        }

        // ═════════════════════════════════════════════════════════════════════
        // FILTRO DE SELEÇÃO
        // ═════════════════════════════════════════════════════════════════════

        public class WallSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is Wall w && w.WallType?.Kind != WallKind.Curtain;
            public bool AllowReference(Reference reference, XYZ position) => true;
        }
    }
}
