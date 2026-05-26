using ARQFlow.Core.Utils;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ARQFlow.Modules.Documentacao.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class CotarAmbientesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            if (view.ViewType != ViewType.FloorPlan && view.ViewType != ViewType.CeilingPlan)
            {
                message = "Este comando só funciona em Planta Baixa ou Planta de Forro.";
                return Result.Failed;
            }

            var ui = new ARQFlow.Modules.Documentacao.Views.CotarAmbientesView(doc);
            if (ui.ShowDialog() != true)
                return Result.Cancelled;

            CotarAmbientesParams p = ui.Parametros;

            List<Room> rooms;
            if (p.Modo == CotarModo.SelecionarManual)
            {
                rooms = SelecionarAmbientes(uidoc, doc);
                if (rooms == null) return Result.Cancelled;
                if (!rooms.Any()) { TaskDialog.Show("Cotar Ambientes", "Nenhum ambiente selecionado."); return Result.Cancelled; }
            }
            else
            {
                rooms = GetRoomsByView(doc, view);
                if (!rooms.Any()) { TaskDialog.Show("Cotar Ambientes", "Nenhum ambiente encontrado na vista atual."); return Result.Succeeded; }
            }

            int cotados = 0;
            using (Transaction tx = new Transaction(doc, "Cotar Ambientes"))
            {
                tx.Start();
                foreach (Room room in rooms)
                {
                    try { if (CotarAmbiente(doc, view, room, p)) cotados++; }
                    catch { }
                }
                tx.Commit();
            }

            TaskDialog.Show("Cotar Ambientes",
                cotados > 0
                    ? $"✔ {cotados} ambiente(s) cotado(s) com sucesso."
                    : "Nenhum ambiente pôde ser cotado.\nVerifique se os ambientes possuem paredes fechadas.");

            return Result.Succeeded;
        }

        // ═════════════════════════════════════════════════════════════════════
        // COLETA DE AMBIENTES
        // ═════════════════════════════════════════════════════════════════════

        private List<Room> SelecionarAmbientes(UIDocument uidoc, Document doc)
        {
            try
            {
                TaskDialog.Show("Selecionar Ambientes",
                    "Selecione os ambientes na planta.\n" +
                    "Clique em concluir para confirmar.");

                IList<Reference> refs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new RoomSelectionFilter(),
                    "Selecione os ambientes a cotar (clique em concluir para confirmar)");

                return refs
                    .Select(r => doc.GetElement(r.ElementId) as Room)
                    .Where(r => r != null && r.Area > 0)
                    .ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return null;
            }
        }

        private List<Room> GetRoomsByView(Document doc, View view)
        {
            return new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .OfType<Room>()
                .Where(r => r.Area > 0)
                .ToList();
        }

        // ═════════════════════════════════════════════════════════════════════
        // COTAGEM PRINCIPAL — escolhe estratégia pelo número de paredes
        // ═════════════════════════════════════════════════════════════════════

        private bool CotarAmbiente(Document doc, View view, Room room, CotarAmbientesParams p)
        {
            var opts = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
            };
            IList<IList<BoundarySegment>> loops = room.GetBoundarySegments(opts);
            if (loops == null || !loops.Any()) return false;

            if (!(room.Location is LocationPoint locPt)) return false;
            XYZ roomCenter = locPt.Point;
            double z = roomCenter.Z;
            double dist = UnitHelper.CmToFeet(p.DistanciaCm);

            // ── Retângulo: apenas paredes eixo-alinhadas + exactamente 2 X e 2 Y  ──
            // ── Forma complexa (L, T, U…): uma cota por segmento de parede       ──
            if (IsRoomRectangular(loops))
                return CotarRetangular(doc, view, loops, roomCenter, z, dist, p.DimensionTypeId, p);
            else
                return CotarPerSegmento(doc, view, loops, roomCenter, z, dist, p.DimensionTypeId);
        }

        // ═════════════════════════════════════════════════════════════════════
        // DETECÇÃO DE RETÂNGULO — geométrica, não por contagem de paredes
        // Um ambiente é retangular se todos os segmentos são eixo-alinhados
        // E há exactamente 2 posições X (paredes verticais) e 2 Y (horizontais).
        // Isso cobre ambientes cujas paredes foram divididas por portas/janelas.
        // ═════════════════════════════════════════════════════════════════════

        private bool IsRoomRectangular(IList<IList<BoundarySegment>> loops)
        {
            var outerLoop = loops[0];
            var xMids = new List<double>();
            var yMids = new List<double>();

            foreach (BoundarySegment seg in outerLoop)
            {
                Curve curve = seg.GetCurve();
                XYZ s = curve.GetEndPoint(0);
                XYZ e = curve.GetEndPoint(1);
                XYZ dir = (e - s).Normalize();

                if (Math.Abs(dir.Y) > 0.95)        // parede vertical  → posição em X
                    xMids.Add(Math.Round((s.X + e.X) / 2.0, 3));
                else if (Math.Abs(dir.X) > 0.95)   // parede horizontal → posição em Y
                    yMids.Add(Math.Round((s.Y + e.Y) / 2.0, 3));
                else
                    return false; // segmento diagonal → não é retângulo
            }

            return xMids.Distinct().Count() == 2 && yMids.Distinct().Count() == 2;
        }

        // ═════════════════════════════════════════════════════════════════════
        // ESTRATÉGIA 1 — RETÂNGULO: busca as paredes extremas em X e Y
        // Cria exatamente 2 cotas (largura + profundidade).
        // ═════════════════════════════════════════════════════════════════════

        private bool CotarRetangular(
            Document doc, View view,
            IList<IList<BoundarySegment>> loops,
            XYZ roomCenter, double z, double dist, ElementId dimTypeId,
            CotarAmbientesParams p)
        {
            bool ok = false;
            bool useMinH = p.PosicaoH == PosicaoHorizontal.Abaixo;  // Abaixo = Y menor
            bool useMinV = p.PosicaoV == PosicaoVertical.Esquerda;   // Esquerda = X menor
            if (TryCriarDimensaoDirecao(doc, view, loops, roomCenter, z, dist, horizontal: true, dimTypeId, useMinH)) ok = true;
            if (TryCriarDimensaoDirecao(doc, view, loops, roomCenter, z, dist, horizontal: false, dimTypeId, useMinV)) ok = true;
            return ok;
        }

        /// <summary>
        /// Cria uma cota horizontal (largura em X) ou vertical (profundidade em Y)
        /// encontrando as paredes mais extremas no eixo medido.
        /// A linha de cota fica a <paramref name="dist"/> da parede oposta, dentro do ambiente.
        /// </summary>
        private bool TryCriarDimensaoDirecao(
            Document doc, View view,
            IList<IList<BoundarySegment>> loops,
            XYZ roomCenter, double z, double dist,
            bool horizontal, ElementId dimTypeId,
            bool useMinSide) // true = Abaixo (Y-) ou Esquerda (X-); false = Acima (Y+) ou Direita (X+)
        {
            var allSegs = loops.SelectMany(l => l).ToList();

            Wall wallNeg = null, wallPos = null;
            double posNeg = double.MaxValue, posPos = double.MinValue;
            double perpAxisMin = double.MaxValue, perpAxisMax = double.MinValue;
            bool foundPerp = false;

            foreach (BoundarySegment seg in allSegs)
            {
                Wall wall = doc.GetElement(seg.ElementId) as Wall;
                if (wall == null) continue;

                Curve curve = seg.GetCurve();
                XYZ s = curve.GetEndPoint(0);
                XYZ e = curve.GetEndPoint(1);
                XYZ dir = (e - s).Normalize();

                if (horizontal)
                {
                    if (Math.Abs(dir.Y) > 0.7) // parede vertical → define largura X
                    {
                        double x = (s.X + e.X) / 2.0;
                        if (x < posNeg) { posNeg = x; wallNeg = wall; }
                        if (x > posPos) { posPos = x; wallPos = wall; }
                    }
                    else if (Math.Abs(dir.X) > 0.7) // parede horizontal → define Y da linha de cota
                    {
                        double y = (s.Y + e.Y) / 2.0;
                        if (y < perpAxisMin) perpAxisMin = y;
                        if (y > perpAxisMax) perpAxisMax = y;
                        foundPerp = true;
                    }
                }
                else
                {
                    if (Math.Abs(dir.X) > 0.7) // parede horizontal → define profundidade Y
                    {
                        double y = (s.Y + e.Y) / 2.0;
                        if (y < posNeg) { posNeg = y; wallNeg = wall; }
                        if (y > posPos) { posPos = y; wallPos = wall; }
                    }
                    else if (Math.Abs(dir.Y) > 0.7) // parede vertical → define X da linha de cota
                    {
                        double x = (s.X + e.X) / 2.0;
                        if (x < perpAxisMin) perpAxisMin = x;
                        if (x > perpAxisMax) perpAxisMax = x;
                        foundPerp = true;
                    }
                }
            }

            if (wallNeg == null || wallPos == null || wallNeg.Id == wallPos.Id) return false;

            double mid = horizontal ? roomCenter.Y : roomCenter.X;
            double linePos;
            if (useMinSide)
            {
                double perpBase = foundPerp ? perpAxisMin : (mid - UnitHelper.CmToFeet(150));
                linePos = Math.Min(perpBase + dist, mid - UnitHelper.CmToFeet(10));
            }
            else
            {
                double perpBase = foundPerp ? perpAxisMax : (mid + UnitHelper.CmToFeet(150));
                linePos = Math.Max(perpBase - dist, mid + UnitHelper.CmToFeet(10));
            }

            Reference ref1 = GetFaceRefFacingPoint(wallNeg, roomCenter);
            Reference ref2 = GetFaceRefFacingPoint(wallPos, roomCenter);
            if (ref1 == null || ref2 == null) return false;

            const double ext = 0.3;
            Line dimLine;
            if (horizontal)
                dimLine = Line.CreateBound(new XYZ(posNeg - ext, linePos, z), new XYZ(posPos + ext, linePos, z));
            else
                dimLine = Line.CreateBound(new XYZ(linePos, posNeg - ext, z), new XYZ(linePos, posPos + ext, z));

            ReferenceArray refs = new ReferenceArray();
            refs.Append(ref1);
            refs.Append(ref2);

            DimensionType dimType = doc.GetElement(dimTypeId) as DimensionType;

            using (SubTransaction st = new SubTransaction(doc))
            {
                st.Start();
                try { doc.Create.NewDimension(view, dimLine, refs, dimType); st.Commit(); return true; }
                catch { st.RollBack(); return false; }
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // ESTRATÉGIA 2 — POR SEGMENTO: uma cota por parede do perímetro
        // ═════════════════════════════════════════════════════════════════════

        private bool CotarPerSegmento(
            Document doc, View view,
            IList<IList<BoundarySegment>> loops,
            XYZ roomCenter, double z, double dist, ElementId dimTypeId)
        {
            var paredesProcessadas = new HashSet<int>();
            int criadas = 0;

            foreach (IList<BoundarySegment> loop in loops)
            {
                int n = loop.Count;
                for (int i = 0; i < n; i++)
                {
                    BoundarySegment seg = loop[i];
                    if (seg.ElementId == ElementId.InvalidElementId) continue;

                    Wall mainWall = doc.GetElement(seg.ElementId) as Wall;
                    if (mainWall == null) continue;
                    if (!paredesProcessadas.Add(seg.ElementId.IntegerValue)) continue;

                    Wall adjA = FindAdjacentInLoop(doc, loop, i, forward: false);
                    Wall adjB = FindAdjacentInLoop(doc, loop, i, forward: true);
                    if (adjA == null || adjB == null) continue;
                    if (adjA.Id == adjB.Id) continue;

                    if (TryCriarDimensaoSegmento(doc, view, seg, adjA, adjB, roomCenter, z, dist, dimTypeId))
                        criadas++;
                }
            }

            return criadas > 0;
        }

        private bool TryCriarDimensaoSegmento(
            Document doc, View view,
            BoundarySegment seg, Wall adjA, Wall adjB,
            XYZ roomCenter, double z, double dist, ElementId dimTypeId)
        {
            Curve curve = seg.GetCurve();
            XYZ ptA = curve.GetEndPoint(0);
            XYZ ptB = curve.GetEndPoint(1);

            if (curve.Length < UnitHelper.CmToFeet(20)) return false;

            XYZ dir = (ptB - ptA).Normalize();
            XYZ perp = new XYZ(-dir.Y, dir.X, 0);
            if (perp.DotProduct(roomCenter - ptA) < 0) perp = perp.Negate();

            XYZ segMid = new XYZ((ptA.X + ptB.X) / 2.0, (ptA.Y + ptB.Y) / 2.0, z);
            XYZ dimMid = new XYZ(segMid.X + perp.X * dist, segMid.Y + perp.Y * dist, z);

            const double ext = 0.3;
            double halfLen = curve.Length / 2.0 + ext;
            XYZ dimStart = new XYZ(dimMid.X - dir.X * halfLen, dimMid.Y - dir.Y * halfLen, z);
            XYZ dimEnd = new XYZ(dimMid.X + dir.X * halfLen, dimMid.Y + dir.Y * halfLen, z);

            Reference refA = GetFaceRefFacingPoint(adjA, segMid);
            Reference refB = GetFaceRefFacingPoint(adjB, segMid);
            if (refA == null || refB == null) return false;

            ReferenceArray refs = new ReferenceArray();
            refs.Append(refA);
            refs.Append(refB);

            DimensionType dimType = doc.GetElement(dimTypeId) as DimensionType;

            using (SubTransaction st = new SubTransaction(doc))
            {
                st.Start();
                try
                {
                    Line dimLine = Line.CreateBound(dimStart, dimEnd);
                    doc.Create.NewDimension(view, dimLine, refs, dimType);
                    st.Commit();
                    return true;
                }
                catch
                {
                    st.RollBack();
                    return false;
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // HELPERS
        // ═════════════════════════════════════════════════════════════════════

        private Wall FindAdjacentInLoop(
            Document doc, IList<BoundarySegment> loop, int index, bool forward)
        {
            int n = loop.Count;
            ElementId currentId = loop[index].ElementId;

            for (int step = 1; step < n; step++)
            {
                int idx = forward ? (index + step) % n : (index - step + n) % n;
                BoundarySegment candidate = loop[idx];

                if (candidate.ElementId == ElementId.InvalidElementId) continue;
                if (candidate.ElementId == currentId) continue;

                Wall wall = doc.GetElement(candidate.ElementId) as Wall;
                if (wall != null) return wall;
            }

            return null;
        }

        private Reference GetFaceRefFacingPoint(Wall wall, XYZ referencePoint)
        {
            try
            {
                if (!(wall.Location is LocationCurve lc)) return null;

                XYZ wallDir = (lc.Curve.GetEndPoint(1) - lc.Curve.GetEndPoint(0)).Normalize();

                var opt = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };

                Reference bestRef = null;
                double bestDot = double.MinValue;

                foreach (GeometryObject gObj in wall.get_Geometry(opt))
                {
                    Solid solid = gObj as Solid;
                    if (solid == null) continue;

                    foreach (Face face in solid.Faces)
                    {
                        if (face.Reference == null) continue;
                        if (!(face is PlanarFace pf)) continue;
                        if (Math.Abs(pf.FaceNormal.Z) > 0.5) continue;
                        if (Math.Abs(pf.FaceNormal.DotProduct(wallDir)) > 0.5) continue;

                        XYZ faceCenter;
                        try { faceCenter = face.Evaluate(new UV(0.5, 0.5)); }
                        catch { continue; }

                        XYZ toRef = referencePoint - faceCenter;
                        if (toRef.GetLength() < 1e-6) continue;

                        double dot = pf.FaceNormal.DotProduct(toRef.Normalize());
                        if (dot > bestDot) { bestDot = dot; bestRef = face.Reference; }
                    }
                }

                return bestRef;
            }
            catch { return null; }
        }

        // ═════════════════════════════════════════════════════════════════════
        // MODELOS DE DADOS
        // ═════════════════════════════════════════════════════════════════════

        public enum CotarModo { TodosVisiveis, SelecionarManual }
        public enum PosicaoHorizontal { Abaixo, Acima }
        public enum PosicaoVertical { Esquerda, Direita }

        public class CotarAmbientesParams
        {
            public ElementId DimensionTypeId { get; set; }
            public CotarModo Modo { get; set; } = CotarModo.TodosVisiveis;
            public double DistanciaCm { get; set; } = 30.0;
            public PosicaoHorizontal PosicaoH { get; set; } = PosicaoHorizontal.Abaixo;
            public PosicaoVertical PosicaoV { get; set; } = PosicaoVertical.Esquerda;
        }

        public class RoomSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is Room;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
