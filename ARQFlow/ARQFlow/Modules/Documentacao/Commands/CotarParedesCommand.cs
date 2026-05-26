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
    public class CotarParedesCommand : IExternalCommand
    {
        // ═════════════════════════════════════════════════════════════════════
        // MODELOS DE DADOS
        // ═════════════════════════════════════════════════════════════════════

        public enum CotarModoParedes { TodosVisiveis, SelecionarManual }
        public enum PosicaoParede { Externo, Interno }

        public class CotarParedesParams
        {
            public ElementId DimensionTypeId { get; set; }
            public CotarModoParedes Modo { get; set; } = CotarModoParedes.TodosVisiveis;
            public double DistanciaCm { get; set; } = 50.0;
            public PosicaoParede Posicao { get; set; } = PosicaoParede.Externo;
            public bool IgnorarPequenas { get; set; } = true;
            public double ComprimentoMinimoCm { get; set; } = 20.0;
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

            var ui = new CotarParedesView(doc);
            if (ui.ShowDialog() != true) return Result.Cancelled;

            CotarParedesParams p = ui.Parametros;

            List<Wall> paredes;
            if (p.Modo == CotarModoParedes.SelecionarManual)
            {
                paredes = SelecionarParedes(uidoc, doc);
                if (paredes == null || !paredes.Any()) return Result.Cancelled;
            }
            else
            {
                paredes = GetParedesVisiveis(doc, view);
                if (!paredes.Any())
                {
                    TaskDialog.Show("Cotar Paredes", "Nenhuma parede encontrada na vista atual.");
                    return Result.Succeeded;
                }
            }

            int cotadas = 0;
            using (Transaction tx = new Transaction(doc, "Cotar Paredes Completas"))
            {
                tx.Start();
                foreach (Wall wall in paredes)
                {
                    try
                    {
                        if (CotarParede(doc, view, wall, p))
                            cotadas++;
                    }
                    catch { /* Parede ignorada em caso de anomalia geométrica */ }
                }
                tx.Commit();
            }

            TaskDialog.Show("Cotar Paredes",
                cotadas > 0
                    ? $"✔ {cotadas} parede(s) cotada(s) com sucesso."
                    : "Nenhuma parede pôde ser cotada.\nVerifique se as paredes possuem aberturas e referências válidas.");

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
                    "Selecione as paredes (Concluir para confirmar)");

                return refs.Select(r => doc.GetElement(r.ElementId) as Wall).ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return null; }
        }

        private List<Wall> GetParedesVisiveis(Document doc, View view)
        {
            // Filtra pelo nível da vista para não cotar paredes de outros pavimentos
            // que aparecem como contexto/projeção na planta do andar atual.
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
        // COTAGEM PRINCIPAL
        // ═════════════════════════════════════════════════════════════════════

        private bool CotarParede(Document doc, View view, Wall wall, CotarParedesParams p)
        {
            if (!(wall.Location is LocationCurve lc)) return false;
            if (!(lc.Curve is Line wallLine)) return false;

            XYZ start = wallLine.GetEndPoint(0);
            XYZ end = wallLine.GetEndPoint(1);
            XYZ dir = (end - start).Normalize();

            // Pula paredes menores que 10 cm
            if (wallLine.Length < UnitUtils.ConvertToInternalUnits(10, UnitTypeId.Centimeters)) return false;

            // Se a opção de ignorar paredes pequenas estiver ativa, pula paredes menores que o comprimento mínimo definido
            if (p.IgnorarPequenas)
            {
                double minLenInternal = UnitUtils.ConvertToInternalUnits(p.ComprimentoMinimoCm, UnitTypeId.Centimeters);
                if (wallLine.Length < minLenInternal) return false;
            }

            // Extrai Extremidades, Vãos de Janelas/Portas e Espessuras de Paredes Conectadas
            List<Reference> references = ObterReferenciasCompletas(doc, view, wall, start, dir);

            // Para existir uma cota, precisamos de pelo menos 2 pontos
            if (references.Count < 2) return false;

            XYZ orientation = wall.Orientation;
            XYZ offsetDirection = p.Posicao == PosicaoParede.Externo ? orientation : orientation.Negate();

            double offsetDist = (wall.Width / 2.0) + UnitUtils.ConvertToInternalUnits(p.DistanciaCm, UnitTypeId.Centimeters);
            XYZ offsetVector = offsetDirection.Multiply(offsetDist);

            // Linha de cota expandida levemente para garantir que intercepte as referências
            XYZ lineStart = start + offsetVector - (dir * 2.0);
            XYZ lineEnd = start + (dir * (wallLine.Length + 2.0)) + offsetVector;
            Line dimLine = Line.CreateBound(lineStart, lineEnd);

            ReferenceArray refArray = new ReferenceArray();
            foreach (Reference r in references) refArray.Append(r);

            DimensionType dimType = doc.GetElement(p.DimensionTypeId) as DimensionType;

            using (SubTransaction st = new SubTransaction(doc))
            {
                st.Start();
                try
                {
                    doc.Create.NewDimension(view, dimLine, refArray, dimType);
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
        // MOTOR DE REFERÊNCIAS (VÃOS + ESPESSURAS)
        // ═════════════════════════════════════════════════════════════════════

        private List<Reference> ObterReferenciasCompletas(Document doc, View view, Wall wall, XYZ start, XYZ dir)
        {
            var itens = new List<(double Posicao, Reference Ref)>();

            // 1. OBTENÇÃO DOS VÃOS E EXTREMIDADES (Via Geometria Sólida da Parede)
            // Usa geometria 3D completa (sem View) para capturar aberturas em qualquer altura,
            // incluindo janelas com peitoril acima do plano de corte da vista (~120 cm).
            // A recursão em GeometryInstance é DESATIVADA (ver ColetarFacesParalelasDaGeometria):
            // ela exporia sólidos individuais das camadas de paredes compostas com faces extras
            // causando segmentos duplicados (ex: 7+7). Os sólidos de nível superior já contêm
            // os cortes de todas as aberturas — a recursão não é necessária para isso.
            // Modifique esta linha dentro do método ObterReferenciasCompletas
            // Modifique a criação do objeto Options para incluir o DetailLevel
            Options opt = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = false,
                DetailLevel = ViewDetailLevel.Coarse // ESSENCIAL: Simplifica paredes e remove camadas
            };
            GeometryElement geomElement = wall.get_Geometry(opt);
            if (geomElement != null)
            {
                var geomItens = new List<(double Posicao, Reference Ref)>();
                ColetarFacesParalelasDaGeometria(geomElement, start, dir, geomItens);

                // Filtra faces fora do intervalo [0, comprimento] ± 2 cm.
                // A recursão sobre GeometryInstance pode incluir geometria de janelas/portas
                // (moldura, guarnição) com faces além das extremidades da parede, gerando
                // referências duplicatas nas bordas e cotas 0 no início e no final.
                double wallLengthGeom = wall.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH).AsDouble();
                double margemGeom = UnitUtils.ConvertToInternalUnits(2.0, UnitTypeId.Centimeters);
                itens.AddRange(geomItens.Where(i => i.Posicao >= -margemGeom && i.Posicao <= wallLengthGeom + margemGeom));
            }

            // 1b. FALLBACK PARA VÃOS: consulta direta às portas/janelas hospedadas na parede.
            // Cobre casos em que a família não gera faces laterais no sólido da parede
            // (famílias customizadas, tipos de parede especiais, geometria aninhada não resolvida).
            // Usa GetReferences(Left/Right) — o mesmo mecanismo do Revit ao cotar manualmente.
            ColetarVaosDeFamilyInstances(doc, wall, start, dir, itens);

            // 2. OBTENÇÃO DAS ESPESSURAS (Paredes que se encontram/interceptam)
            BoundingBoxXYZ bbox = wall.get_BoundingBox(view);
            if (bbox != null)
            {
                // ── CORREÇÃO: Criando o Outline a partir da BoundingBox ──
                Outline outline = new Outline(bbox.Min, bbox.Max);

                // Encontra outras paredes que tocam nesta parede usando o Outline
                FilteredElementCollector intersectingWalls = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(Wall))
                    .Excluding(new[] { wall.Id })
                    .WherePasses(new BoundingBoxIntersectsFilter(outline));

                double wallLength = wall.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH).AsDouble();
                double margem2cm = UnitUtils.ConvertToInternalUnits(2.0, UnitTypeId.Centimeters);

                // Limite inferior generoso (~46 cm): permite capturar a face exterior de uma parede
                // que se conecta no INÍCIO desta parede.
                const double limiteInferior = -1.5; // pés (unidade interna do Revit)

                foreach (Wall interWall in intersectingWalls)
                {
                    try
                    {
                        var sideRefs = HostObjectUtils.GetSideFaces(interWall, ShellLayerType.Exterior)
                            .Concat(HostObjectUtils.GetSideFaces(interWall, ShellLayerType.Interior));

                        var facePairs = new List<(double Pos, Reference Ref)>();
                        foreach (Reference r in sideRefs)
                        {
                            if (interWall.GetGeometryObjectFromReference(r) is PlanarFace pf &&
                                Math.Abs(pf.FaceNormal.DotProduct(dir)) > 0.99)
                            {
                                double pos = (pf.Origin - start).DotProduct(dir);
                                facePairs.Add((pos, r));
                            }
                        }

                        if (facePairs.Count == 0) continue;


                        // Junção de CANTO: uma face fora do span + uma dentro → bug 7+7.
                        // Fix: em canto, inclui apenas a face exterior (fora do span).
                        // Junção em T: ambas as faces dentro do span → inclui as duas (mostra espessura).
                        // Como agora temos um algoritmo de clusterização robusto na Parte 3, 
                        // não precisamos mais ignorar a face interna em junções de canto.
                        // O clusterizador vai agrupar faces sobrepostas automaticamente.
                        double limiteSuperior = wallLength + 1.5; // Limite generoso (em pés) no final da parede

                        foreach (var fd in facePairs)
                        {
                            // Adiciona as faces (internas e externas) da parede transversal
                            if (fd.Pos >= limiteInferior && fd.Pos <= limiteSuperior)
                            {
                                itens.Add(fd);
                            }
                        }
                    }
                    catch { /* parede complexa ou sem faces planar válidas */ }
                }
            }
            // 3. ORDENAÇÃO E LIMPEZA DE DUPLICIDADES (Algoritmo de Clusterização)
            itens = itens.OrderBy(x => x.Posicao).ToList();

            var filtrados = new List<Reference>();
            if (itens.Count > 0)
            {
                // Tolerância de 5cm para engolir acabamentos e batentes teimosos
                double tolerancia = UnitUtils.ConvertToInternalUnits(5.0, UnitTypeId.Centimeters);

                // CORREÇÃO: A tupla agora usa 'Posicao' para manter o padrão da lista 'itens'
                var cluster = new List<(double Posicao, Reference Ref)>();
                cluster.Add(itens[0]);

                for (int i = 1; i < itens.Count; i++)
                {
                    // Se a face atual está a menos de 5cm da ÚLTIMA face do grupo, ela entra no grupo.
                    if (itens[i].Posicao - cluster.Last().Posicao <= tolerancia)
                    {
                        cluster.Add(itens[i]);
                    }
                    else
                    {
                        // Fechou o grupo. Pega a face principal (primeira) e adiciona à linha de cota
                        filtrados.Add(cluster.First().Ref);

                        // Inicia um novo grupo com o item atual
                        cluster.Clear();
                        cluster.Add(itens[i]);
                    }
                }
                // Adiciona o representante do último grupo processado
                filtrados.Add(cluster.First().Ref);
            }

            return filtrados;
        }
        

        // ═════════════════════════════════════════════════════════════════════
        // AUXILIARES DE GEOMETRIA
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Percorre os sólidos de nível superior do GeometryElement e coleta todas as
        /// PlanarFaces cujas normais são paralelas à direção da parede.
        ///
        /// GeometryInstances são IGNORADAS intencionalmente:
        ///   • Com View = null, cada camada de uma parede composta é exposta como um
        ///     GeometryInstance com sólidos próprios. Esses sólidos têm faces de extremidade
        ///     em posições distintas por causa do tratamento de junções, gerando segmentos
        ///     espúrios (ex: 7+7 para uma parede de 14 cm composta por 2 camadas de 7 cm).
        ///   • Os sólidos de nível superior já contêm os cortes de TODAS as aberturas
        ///     (incluindo janelas com peitoril acima do plano de corte da vista), portanto
        ///     a recursão não é necessária para capturar bordas de vãos.
        /// </summary>
        private static void ColetarFacesParalelasDaGeometria(
    GeometryElement geomElement, XYZ start, XYZ dir,
    List<(double Posicao, Reference Ref)> itens)
        {
            // 2.0 sq ft = ~0.18 m². Ignora batentes e molduras, mantém apenas cortes de parede.
            double areaMinima = 2.0;

            foreach (GeometryObject geomObj in geomElement)
            {
                if (geomObj is Solid solid && solid.Faces.Size > 0)
                {
                    foreach (Face face in solid.Faces)
                    {
                        if (face is PlanarFace pf && Math.Abs(pf.FaceNormal.DotProduct(dir)) > 0.99)
                        {
                            if (pf.Area < areaMinima) continue; // Filtro mais agressivo

                            double pos = (pf.Origin - start).DotProduct(dir);
                            itens.Add((pos, pf.Reference));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Fallback: consulta diretamente as portas e janelas hospedadas na parede e obtém
        /// as referências laterais (Left/Right) via FamilyInstance.GetReferences().
        /// Só é usado quando a geometria da parede não encontrou NENHUMA face dentro do vão
        /// da abertura — evitando cotas 0 por referências duplicadas ao mesmo ponto físico.
        ///
        /// A verificação por span completo (e não por borda individual) é necessária porque
        /// o halfWidth estimado via FAMILY_WIDTH_PARAM ou bounding box pode divergir da
        /// abertura real em vários centímetros, tornando a checagem por borda pouco confiável.
        /// </summary>
        private static void ColetarVaosDeFamilyInstances(
            Document doc, Wall wall, XYZ start, XYZ dir,
            List<(double Posicao, Reference Ref)> itens)
        {
            // Margem adicionada ao span da abertura na verificação de cobertura.
            // Absorve a imprecisão do halfWidth estimado (o parâmetro de largura inclui
            // folgas e a bounding box inclui moldura/guarnição).
            double margem = UnitUtils.ConvertToInternalUnits(10.0, UnitTypeId.Centimeters);

            // Faces de extremidade da parede (pos ≈ 0 e pos ≈ wallLength) NÃO devem contar
            // como cobertura de vão — elas representam as pontas da parede, não bordas de abertura.
            // Sem esse filtro, uma janela a 3 cm da ponta seria considerada "coberta" pela face
            // de extremidade e o fallback seria ignorado mesmo sem a borda real encontrada.
            double wallLengthFallback = wall.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH).AsDouble();
            double epsilonExtremidade = UnitUtils.ConvertToInternalUnits(2.0, UnitTypeId.Centimeters);

            var hospedados = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(fi => fi.Host?.Id == wall.Id &&
                             (fi.Category?.Id.Value == (long)BuiltInCategory.OST_Doors ||
                              fi.Category?.Id.Value == (long)BuiltInCategory.OST_Windows));

            foreach (FamilyInstance fi in hospedados)
            {
                try
                {
                    if (!(fi.Location is LocationPoint lp)) continue;
                    double center = (lp.Point - start).DotProduct(dir);

                    // Largura da abertura para estimar o span do vão
                    double halfWidth = 0;
                    Parameter widthParam = fi.get_Parameter(BuiltInParameter.FAMILY_WIDTH_PARAM);
                    if (widthParam != null && widthParam.AsDouble() > 0)
                    {
                        halfWidth = widthParam.AsDouble() / 2.0;
                    }
                    else
                    {
                        // Fallback: usa a projeção da bounding box no eixo da parede
                        BoundingBoxXYZ bb = fi.get_BoundingBox(null);
                        if (bb != null)
                        {
                            double p1 = (bb.Min - start).DotProduct(dir);
                            double p2 = (bb.Max - start).DotProduct(dir);
                            halfWidth = Math.Abs(p2 - p1) / 2.0;
                        }
                    }

                    if (halfWidth <= 0) continue;

                    double spanMin = center - halfWidth - margem;
                    double spanMax = center + halfWidth + margem;

                    // Considera o vão coberto apenas se a geometria encontrou uma face
                    // estritamente DENTRO do corpo da parede (excluindo as extremidades).
                    // Isso evita que a face de extremidade (pos=0 ou pos=wallLength) seja
                    // confundida com uma borda de abertura para janelas próximas às pontas.
                    bool vaoCoberto = itens.Any(i =>
                        i.Posicao > epsilonExtremidade &&
                        i.Posicao < wallLengthFallback - epsilonExtremidade &&
                        i.Posicao > spanMin &&
                        i.Posicao < spanMax);
                    if (vaoCoberto) continue;

                    double posLeft  = center - halfWidth;
                    double posRight = center + halfWidth;

                    foreach (Reference r in fi.GetReferences(FamilyInstanceReferenceType.Left))
                        itens.Add((posLeft, r));

                    foreach (Reference r in fi.GetReferences(FamilyInstanceReferenceType.Right))
                        itens.Add((posRight, r));
                }
                catch { /* Família sem referências padrão Left/Right — ignorada */ }
            }
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