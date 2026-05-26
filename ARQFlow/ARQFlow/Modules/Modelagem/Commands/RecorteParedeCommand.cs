using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ARQFlow.Modules.Modelagem.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RecorteParedeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // --- NOVO: Busca automática pelo primeiro vínculo carregado ---
            // Se você tiver mais de um link, a View WPF precisará permitir a seleção do link correto por lá.
            RevitLinkInstance linkRef = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .FirstOrDefault();

            if (linkRef == null)
            {
                message = "Nenhum vínculo Revit encontrado no projeto.";
                return Result.Failed;
            }

            // ── Abre a janela WPF passando o documento e o link identificado ─────
            var view = new Modules.Modelagem.Views.RecorteParedeView(doc, linkRef.Id);
            bool? resultado = view.ShowDialog();

            if (resultado != true)
                return Result.Cancelled;

            RecorteParedeParams parametros = view.Parametros;

            // Como removemos a seleção manual, esses IDs ficam nulos.
            // O método ExecutarRecortes lidará com isso usando as coordenadas do Host.
            parametros.ElemCalibracaoId = null;
            parametros.WallCalibracaoId = null;

            try
            {
                int count = ExecutarRecortes(doc, parametros);

                TaskDialog.Show("Recortar Paredes",
                    count > 0
                        ? $"✔ {count} abertura(s) criada(s) com sucesso."
                        : "Nenhuma interseção encontrada com as categorias selecionadas.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"Erro ao criar recortes: {ex.Message}";
                return Result.Failed;
            }
        }

        // ═════════════════════════════════════════════════════════════════════════
        // LÓGICA PRINCIPAL (Ajustada para ignorar calibração manual)
        // ═════════════════════════════════════════════════════════════════════════

        private int ExecutarRecortes(Document doc, RecorteParedeParams p)
        {
            RevitLinkInstance linkInstance = doc.GetElement(p.LinkInstanceId) as RevitLinkInstance;
            if (linkInstance == null) throw new InvalidOperationException("Vínculo inválido.");

            Document linkDoc = linkInstance.GetLinkDocument();
            if (linkDoc == null) throw new InvalidOperationException("Documento do vínculo não acessível.");

            IList<Element> linkElements = ColetarElementosDoLink(linkDoc, p.Categorias);

            IList<Wall> paredes = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .Where(w => w.WallType.Kind != WallKind.Curtain)
                .ToList();

            double tol = p.ToleranciaCm / 30.48;
            var aberturas = new List<(Wall parede, XYZ pt1, XYZ pt2)>();
            var chaves = new HashSet<string>();

            // Pegamos a transformação do Link para garantir que o BBox esteja no local certo do Host
            Transform linkTransform = linkInstance.GetTotalTransform();

            foreach (Element linkElem in linkElements)
            {
                // Obtemos o BBox no espaço do link e transformamos para o espaço do Host
                BoundingBoxXYZ bboxLocal = linkElem.get_BoundingBox(null);
                if (bboxLocal == null) continue;

                // Transforma os pontos do BBox para coordenadas globais (Host)
                XYZ minLink = linkTransform.OfPoint(bboxLocal.Min);
                XYZ maxLink = linkTransform.OfPoint(bboxLocal.Max);

                foreach (Wall parede in paredes)
                {
                    BoundingBoxXYZ bboxWall = parede.get_BoundingBox(null);
                    if (bboxWall == null) continue;

                    // Interseção manual simplificada usando os pontos transformados
                    double iMinX = Math.Max(Math.Min(minLink.X, maxLink.X), bboxWall.Min.X);
                    double iMinY = Math.Max(Math.Min(minLink.Y, maxLink.Y), bboxWall.Min.Y);
                    double iMinZ = Math.Max(Math.Min(minLink.Z, maxLink.Z), bboxWall.Min.Z);
                    double iMaxX = Math.Min(Math.Max(minLink.X, maxLink.X), bboxWall.Max.X);
                    double iMaxY = Math.Min(Math.Max(minLink.Y, maxLink.Y), bboxWall.Max.Y);
                    double iMaxZ = Math.Min(Math.Max(minLink.Z, maxLink.Z), bboxWall.Max.Z);

                    if (iMaxX <= iMinX || iMaxY <= iMinY || iMaxZ <= iMinZ) continue;

                    string chave = $"{parede.Id}_{linkElem.Id}";
                    if (!chaves.Add(chave)) continue;

                    (XYZ pt1, XYZ pt2) = CalcularPontosAbertura(
                        parede, new XYZ(iMinX, iMinY, iMinZ), new XYZ(iMaxX, iMaxY, iMaxZ), tol);

                    if (pt1 != null && pt2 != null)
                        aberturas.Add((parede, pt1, pt2));
                }
            }

            int criadas = 0;
            if (!aberturas.Any()) return 0;

            using (Transaction tx = new Transaction(doc, "Recortar Paredes"))
            {
                tx.Start();
                foreach (var (parede, pt1, pt2) in aberturas)
                {
                    try { doc.Create.NewOpening(parede, pt1, pt2); criadas++; }
                    catch { }
                }
                tx.Commit();
            }
            return criadas;
        }


        // ═════════════════════════════════════════════════════════════════════════
        // HELPERS
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Coleta elementos do documento vinculado filtrando pelas categorias
        /// que o usuário marcou na UI.
        ///
        /// Suporta dois cenários:
        ///   1. RVT nativo  → elementos em categorias OST_ normais
        ///   2. IFC → RVT   → elementos viram DirectShape; buscamos por categoria
        ///                    e por palavras-chave no nome da categoria.
        /// </summary>
        private IList<Element> ColetarElementosDoLink(
                Document linkDoc, HashSet<CategoriaRecorte> categorias)
        {
            var resultado = new List<Element>();
            var vistos = new HashSet<ElementId>();

            // ── Mapa 1: categorias nativas OST_ (RVT nativo) ──────────────────
            var mapaOST = new Dictionary<CategoriaRecorte, BuiltInCategory[]>
            {
                [CategoriaRecorte.Estrutura] = new[]
                {
                    BuiltInCategory.OST_StructuralFraming,
                    BuiltInCategory.OST_StructuralColumns,
                    BuiltInCategory.OST_StructuralFoundation,
                    BuiltInCategory.OST_Floors,
                },
                [CategoriaRecorte.Dutos] = new[]
                {
                    BuiltInCategory.OST_DuctCurves,
                    BuiltInCategory.OST_DuctFitting,
                    BuiltInCategory.OST_DuctAccessory,
                },
                [CategoriaRecorte.Tubulacoes] = new[]
                {
                    BuiltInCategory.OST_PipeCurves,
                    BuiltInCategory.OST_PipeFitting,
                    BuiltInCategory.OST_PipeAccessory,
                },
                [CategoriaRecorte.Eletrico] = new[]
                {
                    BuiltInCategory.OST_CableTray,
                    BuiltInCategory.OST_CableTrayFitting,
                    BuiltInCategory.OST_Conduit,
                    BuiltInCategory.OST_ConduitFitting,
                },
            };

            foreach (var cat in categorias)
            {
                if (!mapaOST.TryGetValue(cat, out var builtIns)) continue;
                foreach (var bic in builtIns)
                {
                    try
                    {
                        var elems = new FilteredElementCollector(linkDoc)
                            .OfCategory(bic)
                            .WhereElementIsNotElementType()
                            .ToList();
                        foreach (var e in elems)
                            if (vistos.Add(e.Id)) resultado.Add(e);
                    }
                    catch { /* categoria não existe no link */ }
                }
            }

            // ── Mapa 2: DirectShape por palavras-chave (IFC → RVT) ────────────
            var mapaKeywords = new Dictionary<CategoriaRecorte, string[]>
            {
                [CategoriaRecorte.Estrutura] = new[]
                {
                    "pilar", "coluna", "column",
                    "viga", "beam", "quadro estrutural", "structural framing",
                    "fundaç", "foundation", "laje", "floor", "slab",
                },
                [CategoriaRecorte.Dutos] = new[] { "duto", "duct" },
                [CategoriaRecorte.Tubulacoes] = new[] { "tubo", "pipe", "tubul" },
                [CategoriaRecorte.Eletrico] = new[] { "eletro", "cable", "conduit", "eletrocalha" },
            };

            var todosDShape = new FilteredElementCollector(linkDoc)
                .OfClass(typeof(DirectShape))
                .WhereElementIsNotElementType()
                .ToList();

            foreach (var elem in todosDShape)
            {
                if (!vistos.Contains(elem.Id))
                {
                    string catName = (elem.Category?.Name ?? "").ToLowerInvariant();
                    foreach (var cat in categorias)
                    {
                        if (!mapaKeywords.TryGetValue(cat, out var kws)) continue;
                        if (kws.Any(k => catName.Contains(k)))
                        {
                            vistos.Add(elem.Id);
                            resultado.Add(elem);
                            break;
                        }
                    }
                }
            }

            // ── Fallback: se ainda não achou nada, retorna todos os DirectShapes
            if (!resultado.Any())
            {
                foreach (var e in todosDShape)
                    if (vistos.Add(e.Id)) resultado.Add(e);
            }

            System.Diagnostics.Debug.WriteLine(
                $"[RecorteParede] ColetarElementosDoLink: {resultado.Count} elementos");

            return resultado;
        }

        /// <summary>
        /// Interseção de dois BoundingBoxes em coordenadas do host.
        /// Retorna null se não há sobreposição.
        /// </summary>
        private (XYZ iMin, XYZ iMax)? IntersectarBBoxes(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            if (a == null || b == null) return null;

            double minX = Math.Max(a.Min.X, b.Min.X);
            double minY = Math.Max(a.Min.Y, b.Min.Y);
            double minZ = Math.Max(a.Min.Z, b.Min.Z);
            double maxX = Math.Min(a.Max.X, b.Max.X);
            double maxY = Math.Min(a.Max.Y, b.Max.Y);
            double maxZ = Math.Min(a.Max.Z, b.Max.Z);

            if (maxX <= minX || maxY <= minY || maxZ <= minZ)
                return null;

            return (new XYZ(minX, minY, minZ), new XYZ(maxX, maxY, maxZ));
        }

        /// <summary>
        /// Calcula pt1/pt2 para NewOpening a partir do BBox da interseção.
        /// Os pontos são posicionados ao longo do eixo longitudinal da parede
        /// para garantir que a projeção caia dentro do hospedeiro.
        /// </summary>
        private (XYZ pt1, XYZ pt2) CalcularPontosAbertura(
            Wall wall, XYZ iMin, XYZ iMax, double tol)
        {
            try
            {
                XYZ centro = new XYZ(
                    (iMin.X + iMax.X) / 2,
                    (iMin.Y + iMax.Y) / 2,
                    (iMin.Z + iMax.Z) / 2);

                double largura = Math.Abs(iMax.X - iMin.X) + tol * 2;
                double altura = Math.Abs(iMax.Z - iMin.Z) + tol * 2;

                // Direção longitudinal da parede
                XYZ wallDir = XYZ.BasisX;
                if (wall.Location is LocationCurve lc)
                {
                    XYZ s = lc.Curve.GetEndPoint(0);
                    XYZ e = lc.Curve.GetEndPoint(1);
                    wallDir = (e - s).Normalize();
                }

                XYZ pt1 = new XYZ(
                    centro.X - wallDir.X * largura / 2,
                    centro.Y - wallDir.Y * largura / 2,
                    centro.Z - altura / 2);

                XYZ pt2 = new XYZ(
                    centro.X + wallDir.X * largura / 2,
                    centro.Y + wallDir.Y * largura / 2,
                    centro.Z + altura / 2);

                return (pt1, pt2);
            }
            catch
            {
                return (null, null);
            }
        }

        // ═════════════════════════════════════════════════════════════════════════════
        // MODELOS DE DADOS (compartilhados entre Command e View)
        // ═════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Categorias que o usuário pode habilitar na UI.
        /// </summary>
        public enum CategoriaRecorte
        {
            Estrutura,
            Dutos,
            Tubulacoes,
            Eletrico
        }

        /// <summary>
        /// Parâmetros preenchidos pela View e consumidos pelo Command.
        /// </summary>
        public class RecorteParedeParams
        {
            public ElementId LinkInstanceId { get; set; }
            public HashSet<CategoriaRecorte> Categorias { get; set; } = new HashSet<CategoriaRecorte>();
            public double ToleranciaCm { get; set; } = 2.0;
            // Par de calibração: elemento do link + parede do host que ele atravessa
            // Usado para calcular o offset quando o link não tem Shared Coordinates
            public ElementId ElemCalibracaoId { get; set; }
            public ElementId WallCalibracaoId { get; set; }
        }

        public class RecorteWallSelectionFilter : Autodesk.Revit.UI.Selection.ISelectionFilter
        {
            public bool AllowElement(Element elem) =>
                elem is Wall w && w.WallType?.Kind != WallKind.Curtain;
            public bool AllowReference(
                Autodesk.Revit.DB.Reference reference, XYZ position) => false;
        }
    }
}