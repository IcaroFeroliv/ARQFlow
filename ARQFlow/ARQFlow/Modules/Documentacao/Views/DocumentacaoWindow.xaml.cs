using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace ARQFlow.Modules.Documentacao.Views
{
    public partial class DocumentacaoWindow : Window
    {
        private readonly Document _doc;
        private readonly ExternalCommandData _commandData;

        public DocumentacaoWindow(ExternalCommandData commandData)
        {
            InitializeComponent();
            _commandData = commandData;
            _doc = commandData.Application.ActiveUIDocument.Document;
            CarregarFamilias();
        }

        private void CarregarFamilias()
        {
            /* var tiposCota = new FilteredElementCollector(_doc)
                .OfClass(typeof(DimensionType))
                .Cast<DimensionType>()
                .OrderBy(x => x.Name)
                .ToList();
            cbCotas.ItemsSource = tiposCota;
            cbCotas.SelectedIndex = -1; */

            var tagsPorta = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_DoorTags)
                .Cast<FamilySymbol>()
                .OrderBy(x => x.Name)
                .ToList();
            cbPortas.ItemsSource = tagsPorta;
            cbPortas.SelectedIndex = -1;

            var tagsJanela = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_WindowTags)
                .Cast<FamilySymbol>()
                .OrderBy(x => x.Name)
                .ToList();
            cbJanelas.ItemsSource = tagsJanela;
            cbJanelas.SelectedIndex = -1;

            var spotTypes = new FilteredElementCollector(_doc)
                .OfClass(typeof(SpotDimensionType))
                .Cast<SpotDimensionType>()
                .OrderBy(x => x.Name)
                .ToList();
            cbNiveis.ItemsSource = spotTypes;
            cbNiveis.SelectedIndex = -1;
        }

        private void BtnExecutar_Click(object sender, RoutedEventArgs e)
        {
            using (Transaction trans = new Transaction(_doc, "Documentação Automática"))
            {
                try
                {
                    trans.Start();

                    var tipoNivel = cbNiveis.SelectedItem as SpotDimensionType;
                    if (tipoNivel != null)
                    {
                        bool vChamada = chkAtivarChamada.IsChecked ?? false;
                        bool vRessalto = chkRessalto.IsChecked ?? false;
                        InserirNiveisNosPisos(tipoNivel, vChamada, vRessalto);
                    }

                    var tagPorta = cbPortas.SelectedItem as FamilySymbol;
                    string posPorta = (cbPosPorta.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Centro";
                    if (tagPorta != null) InserirTags(BuiltInCategory.OST_Doors, tagPorta, posPorta);

                    var tagJanela = cbJanelas.SelectedItem as FamilySymbol;
                    if (tagJanela != null) InserirTags(BuiltInCategory.OST_Windows, tagJanela, "Centro");

                    /* var tipoCota = cbCotas.SelectedItem as DimensionType;
                    if (tipoCota != null)
                    {
                        CotasExternasGeraisInteligente(tipoCota);
                        InserirCotasInternas(tipoCota);
                    } */

                    var fho = trans.GetFailureHandlingOptions();
                    fho.SetFailuresPreprocessor(new DimFailurePreprocessor());
                    fho.SetClearAfterRollback(true);
                    trans.SetFailureHandlingOptions(fho);

                    trans.Commit();
                    Close();
                }
                catch (Exception ex)
                {
                    if (trans.GetStatus() == TransactionStatus.Started) trans.RollBack();
                    TaskDialog.Show("Erro", "Falha ao processar:\n\n" + ex);
                }
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e) => Close();

        // ============================================================
        // NÍVEIS (sem alteração)
        // ============================================================
        private void InserirNiveisNosPisos(SpotDimensionType spotType, bool ativarChamada, bool ativarRessalto)
        {
            View activeView = _doc.ActiveView;
            var ambientes = new FilteredElementCollector(_doc, activeView.Id)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .Cast<SpatialElement>();

            var todosPisos = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType()
                .ToElements();

            foreach (SpatialElement rm in ambientes)
            {
                LocationPoint locPt = rm.Location as LocationPoint;
                if (locPt == null) continue;

                // Candidato: face com normal para cima de maior Z que projeta o ponto do ambiente
                // Isso garante que sempre pegamos o TOPO do piso, mesmo em pisos multicamada.
                Face melhorFace = null;
                double melhorZ = double.NegativeInfinity;
                XYZ melhorPonto = null;

                foreach (Element piso in todosPisos)
                {
                    Options opt = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
                    GeometryElement geoElem = piso.get_Geometry(opt);
                    if (geoElem == null) continue;

                    foreach (GeometryObject obj in geoElem)
                    {
                        if (obj is not Solid solid || solid.Volume <= 0) continue;

                        foreach (Face face in solid.Faces)
                        {
                            // Exige normal apontando para cima
                            if (face.ComputeNormal(new UV(0.5, 0.5)).Z < 0.9) continue;

                            // Projeta o ponto do ambiente sobre a face
                            IntersectionResult result = face.Project(locPt.Point);
                            if (result == null) continue;

                            XYZ ponto = result.XYZPoint;

                            // Aceita apenas faces dentro de 4ft (vertical) do ponto do room
                            if (Math.Abs(ponto.Z - locPt.Point.Z) > 4.0) continue;

                            // Guarda a face de MAIOR Z (topo real do piso)
                            if (ponto.Z > melhorZ)
                            {
                                melhorZ = ponto.Z;
                                melhorFace = face;
                                melhorPonto = ponto;
                            }
                        }
                    }
                }

                // Cria o spot elevation usando a face de topo encontrada
                if (melhorFace != null && melhorPonto != null)
                {
                    XYZ pI = melhorPonto;
                    XYZ pE = pI + new XYZ(0.4, 0.4, 0);

                    SpotDimension sd = _doc.Create.NewSpotElevation(
                        activeView, melhorFace.Reference, pI, pE, pE, pI, true);
                    if (sd == null) goto ProximoAmbiente;

                    sd.ChangeTypeId(spotType.Id);

                    using (SubTransaction st = new SubTransaction(_doc))
                    {
                        st.Start();
                        SetParamValue(sd, "ressalto", false);
                        SetParamValue(sd, "shoulder", false);
                        SetParamValue(sd, "ombro", false);
                        st.Commit();
                    }
                    _doc.Regenerate();

                    if (ativarRessalto && ativarChamada)
                    {
                        using (SubTransaction st = new SubTransaction(_doc))
                        {
                            st.Start();
                            SetParamValue(sd, "ressalto", true);
                            SetParamValue(sd, "shoulder", true);
                            st.Commit();
                        }
                    }

                    if (!ativarChamada)
                    {
                        using (SubTransaction st = new SubTransaction(_doc))
                        {
                            st.Start();
                            SetParamValue(sd, "chamada", false);
                            SetParamValue(sd, "leader", false);
                            try { sd.LeaderEndPosition = pI; } catch { }
                            st.Commit();
                        }
                    }
                }

            ProximoAmbiente:;
            }
        }

        private void SetParamValue(SpotDimension sd, string paramName, bool value)
        {
            foreach (Parameter p in sd.Parameters)
            {
                if (p.IsReadOnly) continue;
                if (p.Definition.Name.ToLower().Contains(paramName.ToLower()))
                    p.Set(value ? 1 : 0);
            }
        }

        // ============================================================
        // TAGS (sem alteração)
        // ============================================================
        private void InserirTags(BuiltInCategory categoria, FamilySymbol tipoTag, string posicao = "Centro")
        {
            View activeView = _doc.ActiveView;
            if (!tipoTag.IsActive) tipoTag.Activate();

            var elementos = new FilteredElementCollector(_doc, activeView.Id)
                .OfCategory(categoria)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .ToList();

            if (elementos.Count == 0) return;

            double sumX = elementos.Average(e => (e.Location as LocationPoint).Point.X);
            double sumY = elementos.Average(e => (e.Location as LocationPoint).Point.Y);
            XYZ centroProjeto = new XYZ(sumX, sumY, 0);

            foreach (FamilyInstance instance in elementos)
            {
                bool jaTemTag = new FilteredElementCollector(_doc, activeView.Id)
                    .OfClass(typeof(IndependentTag))
                    .Cast<IndependentTag>()
                    .Any(t => t.GetTaggedElementIds().Select(link => link.HostElementId).Contains(instance.Id));

                if (jaTemTag) continue;

                LocationPoint lp = instance.Location as LocationPoint;
                if (lp == null) continue;

                XYZ pontoBase = lp.Point;
                XYZ pontoInsercao = pontoBase;

                if (categoria == BuiltInCategory.OST_Windows)
                {
                    Wall parede = instance.Host as Wall;
                    if (parede != null && parede.Location is LocationCurve wallLine)
                    {
                        XYZ vetorParede = (wallLine.Curve.GetEndPoint(1) - wallLine.Curve.GetEndPoint(0)).Normalize();
                        XYZ normalParede = new XYZ(-vetorParede.Y, vetorParede.X, 0);
                        XYZ vetorParaCentro = (centroProjeto - pontoBase).Normalize();
                        if (normalParede.DotProduct(vetorParaCentro) > 0) normalParede = -normalParede;
                        pontoInsercao = pontoBase + (normalParede * 1.3);
                    }
                }
                else if (categoria == BuiltInCategory.OST_Doors && posicao != "Centro")
                {
                    Wall parede = instance.Host as Wall;
                    if (parede != null && parede.Location is LocationCurve wallLineDoor)
                    {
                        // Calcula a perpendicular da parede no plano XY
                        XYZ vetorParede = (wallLineDoor.Curve.GetEndPoint(1) - wallLineDoor.Curve.GetEndPoint(0)).Normalize();
                        XYZ normal = new XYZ(-vetorParede.Y, vetorParede.X, 0).Normalize();

                        // Normaliza para sempre apontar para Y+ (norte do projeto).
                        // Para paredes quase N-S (|Y| pequeno), usa X+ como referência.
                        // Isso elimina a dependência do sentido de desenho da parede.
                        if (Math.Abs(normal.Y) > 0.1)
                        {
                            if (normal.Y < 0) normal = -normal;
                        }
                        else
                        {
                            if (normal.X < 0) normal = -normal;
                        }

                        double offsetFt = 1.0; // ~30 cm
                        if (posicao == "Acima")
                            pontoInsercao = pontoBase + (normal * offsetFt);
                        else if (posicao == "Abaixo")
                            pontoInsercao = pontoBase - (normal * offsetFt);
                    }
                }

                IndependentTag.Create(_doc, tipoTag.Id, activeView.Id, new Reference(instance), false, TagOrientation.Horizontal, pontoInsercao);
            }
        }

        // ============================================================
        // COTAS EXTERNAS (sem alteração)
        // ============================================================
        private void CotasExternasGeraisInteligente(DimensionType dimType)
        {
            View view = _doc.ActiveView;
            if (view is not ViewPlan vp) return;

            Level lvl = vp.GenLevel;
            double z = (lvl != null) ? lvl.Elevation : view.Origin.Z;

            var wallsAll = new FilteredElementCollector(_doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .Cast<Wall>()
                .Where(w => w.Location is LocationCurve)
                .ToList();

            if (wallsAll.Count < 2) return;

            var walls = GetLargestConnectedWallGroup(view, wallsAll, tolFt: 0.5);
            if (walls.Count < 2) return;

            Reference rLeft, rRight, rBottom, rTop;
            double xLeft, xRight, yBottom, yTop;

            if (!TryGetExtremeFaceRef(walls, view, wantMax: false, axisX: true, out rLeft, out xLeft)) return;
            if (!TryGetExtremeFaceRef(walls, view, wantMax: true, axisX: true, out rRight, out xRight)) return;
            if (!TryGetExtremeFaceRef(walls, view, wantMax: false, axisX: false, out rBottom, out yBottom)) return;
            if (!TryGetExtremeFaceRef(walls, view, wantMax: true, axisX: false, out rTop, out yTop)) return;

            double offset = 2.0;
            double extra = 1.0;

            double yLine = TryGetFacePointAndNormal(rBottom, out _, out var nB) && Math.Abs(nB.Y) > 0.5
                ? yBottom + Math.Sign(nB.Y) * offset
                : yBottom - offset;

            Line lineH = Line.CreateBound(new XYZ(xLeft - extra, yLine, z), new XYZ(xRight + extra, yLine, z));
            Dimension dH = _doc.Create.NewDimension(view, lineH, MakeRefArray(rLeft, rRight));
            if (dH != null) dH.ChangeTypeId(dimType.Id);

            double xLine = TryGetFacePointAndNormal(rRight, out _, out var nR) && Math.Abs(nR.X) > 0.5
                ? xRight + Math.Sign(nR.X) * offset
                : xRight + offset;

            Line lineV = Line.CreateBound(new XYZ(xLine, yBottom - extra, z), new XYZ(xLine, yTop + extra, z));
            Dimension dV = _doc.Create.NewDimension(view, lineV, MakeRefArray(rBottom, rTop));
            if (dV != null) dV.ChangeTypeId(dimType.Id);

            _doc.Regenerate();
        }

        // ============================================================
        // COTAS INTERNAS v5 — posicionamento inteligente
        // ============================================================
        private void InserirCotasInternas(DimensionType dimType)
        {
            View view = _doc.ActiveView;
            if (view is not ViewPlan)
            {
                TaskDialog.Show("Cotas internas", "Abra uma vista de PLANTA (ViewPlan).");
                return;
            }

            double cutZ = GetCutPlaneZ(view);

            var allRooms = new FilteredElementCollector(_doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .OfType<Room>()
                .Where(r => r.Area > 0)
                .ToList();

            if (allRooms.Count == 0)
            {
                TaskDialog.Show("Cotas internas", "Nenhum ambiente (Room) encontrado na vista.");
                return;
            }

            // Pré-calcula o centro do projeto e o cache de rooms uma só vez
            XYZ projetoCenter = GetProjectCenter(allRooms);

            int totalCreated = 0;
            foreach (var room in allRooms)
            {
                try { totalCreated += CotarAmbiente(view, cutZ, room, dimType, allRooms, projetoCenter); }
                catch { }
            }

            _doc.Regenerate();
            TaskDialog.Show("Cotas internas", $"✅ Criadas: {totalCreated} cota(s) em {allRooms.Count} ambiente(s).");
        }

        private int CotarAmbiente(View view, double cutZ, Room room, DimensionType dimType,
                                   List<Room> todosRooms, XYZ projetoCenter)
        {
            int created = 0;

            // ── 1. Paredes do contorno via GetBoundarySegments ───────────────
            var spatialOpts = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
            };
            var boundaries = room.GetBoundarySegments(spatialOpts);
            if (boundaries == null || boundaries.Count == 0) return created;

            var seenIds = new HashSet<ElementId>();
            var paredes = new List<Wall>();
            foreach (var loop in boundaries)
                foreach (var seg in loop)
                {
                    if (seg.ElementId == ElementId.InvalidElementId) continue;
                    if (seenIds.Contains(seg.ElementId)) continue;
                    if (_doc.GetElement(seg.ElementId) is Wall w && w.Location is LocationCurve)
                    { paredes.Add(w); seenIds.Add(seg.ElementId); }
                }

            if (paredes.Count < 2) return created;

            // ── 2. Centro do ambiente ────────────────────────────────────────
            LocationPoint roomLoc = room.Location as LocationPoint;
            XYZ roomCenter = roomLoc?.Point ?? XYZ.Zero;

            // ── 3. Faces internas separadas por eixo ─────────────────────────
            var facesX = new List<(Reference Ref, double PosX, double PosY)>(); // normal ~ X
            var facesY = new List<(Reference Ref, double PosX, double PosY)>(); // normal ~ Y

            foreach (var wall in paredes)
            {
                Reference faceRef = ObterFaceInterna(wall, roomCenter);
                if (faceRef == null) continue;
                try
                {
                    var geo = _doc.GetElement(faceRef.ElementId).GetGeometryObjectFromReference(faceRef);
                    if (geo is not Face face) continue;
                    BoundingBoxUV bbUV = face.GetBoundingBox();
                    if (bbUV == null) continue;
                    UV mid = new UV((bbUV.Min.U + bbUV.Max.U) * 0.5, (bbUV.Min.V + bbUV.Max.V) * 0.5);
                    XYZ fc = face.Evaluate(mid);
                    XYZ fn = face.ComputeNormal(mid).Normalize();
                    if (Math.Abs(fn.Z) > 0.3) continue;
                    double ax = Math.Abs(fn.X), ay = Math.Abs(fn.Y);
                    if (ax < 0.6 && ay < 0.6) continue;
                    if (ax >= ay) facesX.Add((faceRef, fc.X, fc.Y));
                    else facesY.Add((faceRef, fc.X, fc.Y));
                }
                catch { }
            }

            // ── 4. Bounding box do ambiente ──────────────────────────────────
            BoundingBoxXYZ roomBB = room.get_BoundingBox(null);
            if (roomBB == null) return created;

            double bbMinX = roomBB.Min.X, bbMaxX = roomBB.Max.X;
            double bbMinY = roomBB.Min.Y, bbMaxY = roomBB.Max.Y;
            double bbCenX = (bbMinX + bbMaxX) / 2.0;
            double bbCenY = (bbMinY + bbMaxY) / 2.0;

            // Parâmetros de distância
            double offsetExtFt = MmToFt(350); // linha fora do ambiente
            double offsetIntFt = MmToFt(300); // linha dentro (fallback), afastada da parede
            double extFt = MmToFt(150); // extensão extra nas pontas
            double espacoMinFt = MmToFt(500); // espaço livre mínimo para ir para fora

            // Rooms vizinhos (exceto o próprio) — usados para medir espaço livre
            var vizinhos = todosRooms.Where(r => r.Id != room.Id).ToList();

            // ── 5. Cota horizontal (mede largura X) ──────────────────────────
            if (facesX.Count >= 2)
            {
                var sortedX = facesX.OrderBy(f => f.PosX).ToList();
                double distX = Math.Abs(sortedX.Last().PosX - sortedX.First().PosX);

                if (distX >= MmToFt(150))
                {
                    double xDe = bbMinX - extFt;
                    double xAte = bbMaxX + extFt;

                    // Lado preferido: o mais distante do centro do projeto no eixo Y
                    // (lado mais "externo" da planta)
                    bool preferirAbaixo = projetoCenter.Y >= bbCenY;
                    double yLinha = EscolherLadoY(
                        preferirAbaixo, bbMinY, bbMaxY, bbCenY,
                        bbMinX, bbMaxX, vizinhos,
                        offsetExtFt, offsetIntFt, espacoMinFt);

                    try
                    {
                        Line dimLine = Line.CreateBound(new XYZ(xDe, yLinha, cutZ), new XYZ(xAte, yLinha, cutZ));
                        var ra = new ReferenceArray();
                        ra.Append(sortedX.First().Ref);
                        ra.Append(sortedX.Last().Ref);
                        Dimension dim = _doc.Create.NewDimension(view, dimLine, ra);
                        if (dim != null) { dim.ChangeTypeId(dimType.Id); _doc.Regenerate(); created++; }
                    }
                    catch { }
                }
            }

            // ── 6. Cota vertical (mede comprimento Y) ────────────────────────
            if (facesY.Count >= 2)
            {
                var sortedY = facesY.OrderBy(f => f.PosY).ToList();
                double distY = Math.Abs(sortedY.Last().PosY - sortedY.First().PosY);

                if (distY >= MmToFt(150))
                {
                    double yDe = bbMinY - extFt;
                    double yAte = bbMaxY + extFt;

                    // Lado preferido: o mais distante do centro do projeto no eixo X
                    bool preferirEsquerda = projetoCenter.X >= bbCenX;
                    double xLinha = EscolherLadoX(
                        preferirEsquerda, bbMinX, bbMaxX, bbCenX,
                        bbMinY, bbMaxY, vizinhos,
                        offsetExtFt, offsetIntFt, espacoMinFt);

                    try
                    {
                        Line dimLine = Line.CreateBound(new XYZ(xLinha, yDe, cutZ), new XYZ(xLinha, yAte, cutZ));
                        var ra = new ReferenceArray();
                        ra.Append(sortedY.First().Ref);
                        ra.Append(sortedY.Last().Ref);
                        Dimension dim = _doc.Create.NewDimension(view, dimLine, ra);
                        if (dim != null) { dim.ChangeTypeId(dimType.Id); _doc.Regenerate(); created++; }
                    }
                    catch { }
                }
            }

            return created;
        }

        // ── Decide a coordenada Y da linha de cota horizontal ────────────────
        // Tenta o lado externo preferido. Se não houver espaço, tenta o lado
        // oposto externo. Se nenhum tiver espaço, coloca dentro do ambiente.
        private double EscolherLadoY(bool preferirAbaixo, double bbMinY, double bbMaxY, double bbCenY, double bbMinX, double bbMaxX, List<Room> vizinhos, double offsetExt, double offsetInt, double espacoMin)
        {
            double espacoAbaixo = EspacoLivre(vizinhos, bbMinX, bbMaxX, bbMinY, bbMaxY, direcao: -1, eixoY: true);
            double espacoAcima = EspacoLivre(vizinhos, bbMinX, bbMaxX, bbMinY, bbMaxY, direcao: +1, eixoY: true);

            if (preferirAbaixo)
            {
                if (espacoAbaixo >= espacoMin) return bbMinY - offsetExt;  // externo abaixo ✅
                if (espacoAcima >= espacoMin) return bbMaxY + offsetExt;  // externo acima  ✅
            }
            else
            {
                if (espacoAcima >= espacoMin) return bbMaxY + offsetExt;  // externo acima  ✅
                if (espacoAbaixo >= espacoMin) return bbMinY - offsetExt;  // externo abaixo ✅
            }

            // Nenhum lado externo tem espaço → coloca dentro do ambiente
            // Posiciona próximo à parede do lado preferido, dentro do cômodo
            return preferirAbaixo
                ? bbMinY + offsetInt   // dentro, perto da parede inferior
                : bbMaxY - offsetInt;  // dentro, perto da parede superior
        }

        // ── Decide a coordenada X da linha de cota vertical ──────────────────
        private double EscolherLadoX(bool preferirEsquerda, double bbMinX, double bbMaxX, double bbCenX, double bbMinY, double bbMaxY, List<Room> vizinhos, double offsetExt, double offsetInt, double espacoMin)
        {
            double espacoEsq = EspacoLivre(vizinhos, bbMinY, bbMaxY, bbMinX, bbMaxX, direcao: -1, eixoY: false);
            double espacoDir = EspacoLivre(vizinhos, bbMinY, bbMaxY, bbMinX, bbMaxX, direcao: +1, eixoY: false);

            if (preferirEsquerda)
            {
                if (espacoEsq >= espacoMin) return bbMinX - offsetExt;  // externo esquerda ✅
                if (espacoDir >= espacoMin) return bbMaxX + offsetExt;  // externo direita  ✅
            }
            else
            {
                if (espacoDir >= espacoMin) return bbMaxX + offsetExt;  // externo direita  ✅
                if (espacoEsq >= espacoMin) return bbMinX - offsetExt;  // externo esquerda ✅
            }

            // Dentro do ambiente
            return preferirEsquerda
                ? bbMinX + offsetInt   // dentro, perto da parede esquerda
                : bbMaxX - offsetInt;  // dentro, perto da parede direita
        }

        // ── Mede o espaço livre entre a face do ambiente e o vizinho mais próximo
        // direcao: -1 = min (abaixo/esquerda), +1 = max (acima/direita)
        // eixoY: true = mede em Y (para cotas horizontais), false = mede em X
        private double EspacoLivre(List<Room> vizinhos, double perpMin, double perpMax, double parMin, double parMax, int direcao, bool eixoY)
        {
            double menorDist = double.MaxValue;

            foreach (var viz in vizinhos)
            {
                BoundingBoxXYZ bb = viz.get_BoundingBox(null);
                if (bb == null) continue;

                // Verifica sobreposição no eixo perpendicular
                double vizPerpMin = eixoY ? bb.Min.X : bb.Min.Y;
                double vizPerpMax = eixoY ? bb.Max.X : bb.Max.Y;
                if (vizPerpMax < perpMin || vizPerpMin > perpMax) continue;

                // Mede distância no eixo paralelo
                double vizParMin = eixoY ? bb.Min.Y : bb.Min.X;
                double vizParMax = eixoY ? bb.Max.Y : bb.Max.X;

                if (direcao == -1) // procura vizinhos abaixo/à esquerda
                {
                    if (vizParMax <= parMin)
                        menorDist = Math.Min(menorDist, parMin - vizParMax);
                }
                else // procura vizinhos acima/à direita
                {
                    if (vizParMin >= parMax)
                        menorDist = Math.Min(menorDist, vizParMin - parMax);
                }
            }

            // Se não há vizinho nessa direção, espaço é infinito (totalmente livre)
            return menorDist == double.MaxValue ? double.MaxValue : menorDist;
        }

        // ── Centro médio de todos os rooms (define "interior" do projeto) ────
        private XYZ GetProjectCenter(List<Room> rooms)
        {
            double sx = 0, sy = 0;
            int count = 0;
            foreach (var r in rooms)
            {
                LocationPoint lp = r.Location as LocationPoint;
                if (lp == null) continue;
                sx += lp.Point.X;
                sy += lp.Point.Y;
                count++;
            }
            return count > 0 ? new XYZ(sx / count, sy / count, 0) : XYZ.Zero;
        }

        // ── Retorna a face lateral da parede que aponta para o roomCenter ────
        private Reference ObterFaceInterna(Wall wall, XYZ roomCenter)
        {
            try
            {
                var candidatos = new List<Reference>();
                var interior = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Interior);
                var exterior = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Exterior);
                if (interior != null) candidatos.AddRange(interior);
                if (exterior != null) candidatos.AddRange(exterior);

                Reference melhor = null;
                double melhorDot = double.NegativeInfinity;

                foreach (var r in candidatos)
                {
                    try
                    {
                        var geo = _doc.GetElement(r.ElementId).GetGeometryObjectFromReference(r);
                        if (geo is not Face face) continue;
                        BoundingBoxUV bb = face.GetBoundingBox();
                        if (bb == null) continue;
                        UV mid = new UV((bb.Min.U + bb.Max.U) * 0.5, (bb.Min.V + bb.Max.V) * 0.5);
                        XYZ fc = face.Evaluate(mid);
                        XYZ fn = face.ComputeNormal(mid).Normalize();
                        if (Math.Abs(fn.Z) > 0.3) continue;
                        XYZ toCenter = roomCenter - fc;
                        if (toCenter.GetLength() < 0.001) continue;
                        double dot = fn.DotProduct(toCenter.Normalize());
                        if (dot > melhorDot) { melhorDot = dot; melhor = r; }
                    }
                    catch { }
                }
                return melhor;
            }
            catch { return null; }
        }

        // ── Z do plano de corte real da vista ────────────────────────────────
        private double GetCutPlaneZ(View view)
        {
            try
            {
                if (view is ViewPlan vp)
                {
                    PlanViewRange pvr = vp.GetViewRange();
                    double cutOffset = pvr.GetOffset(PlanViewPlane.CutPlane);
                    Level level = _doc.GetElement(pvr.GetLevelId(PlanViewPlane.CutPlane)) as Level ?? vp.GenLevel;
                    return (level?.Elevation ?? 0) + cutOffset;
                }
            }
            catch { }
            return view.Origin.Z;
        }

        // ============================================================
        // Helpers / Infra (sem alteração)
        // ============================================================
        private static double MmToFt(double mm) => mm / 304.8;

        private ReferenceArray MakeRefArray(Reference a, Reference b)
        {
            var ra = new ReferenceArray();
            ra.Append(a); ra.Append(b);
            return ra;
        }

        private bool TryGetFacePointAndNormal(Reference r, out XYZ point, out XYZ normal)
        {
            point = null; normal = null;
            try
            {
                var faceObj = _doc.GetElement(r.ElementId).GetGeometryObjectFromReference(r);
                if (faceObj is not Face face) return false;
                UV uv = new UV(0.5, 0.5);
                point = face.Evaluate(uv);
                normal = face.ComputeNormal(uv);
                return true;
            }
            catch { return false; }
        }

        private bool TryGetExtremeFaceRef(IEnumerable<Wall> walls, View view, bool wantMax, bool axisX, out Reference bestRef, out double bestCoord)
        {
            bestRef = null;
            bestCoord = wantMax ? double.NegativeInfinity : double.PositiveInfinity;

            foreach (var w in walls)
            {
                var candidates = new List<Reference>();
                try
                {
                    var ext = HostObjectUtils.GetSideFaces(w, ShellLayerType.Exterior);
                    var inte = HostObjectUtils.GetSideFaces(w, ShellLayerType.Interior);
                    if (ext != null) candidates.AddRange(ext);
                    if (inte != null) candidates.AddRange(inte);
                }
                catch { continue; }

                foreach (var r in candidates)
                {
                    try
                    {
                        var faceObj = _doc.GetElement(r.ElementId).GetGeometryObjectFromReference(r);
                        if (faceObj is not Face face) continue;
                        XYZ n = face.ComputeNormal(new UV(0.5, 0.5));
                        if (Math.Abs(n.Z) > 0.01) continue;
                        if (axisX) { if (Math.Abs(n.X) < 0.9) continue; }
                        else { if (Math.Abs(n.Y) < 0.9) continue; }
                        XYZ p = face.Evaluate(new UV(0.5, 0.5));
                        double coord = axisX ? p.X : p.Y;
                        if (wantMax) { if (coord > bestCoord) { bestCoord = coord; bestRef = r; } }
                        else { if (coord < bestCoord) { bestCoord = coord; bestRef = r; } }
                    }
                    catch { }
                }
            }
            return bestRef != null;
        }

        private List<Wall> GetLargestConnectedWallGroup(View view, List<Wall> walls, double tolFt = 0.3)
        {
            var endpoints = new Dictionary<ElementId, (XYZ A, XYZ B)>();
            foreach (var w in walls)
            {
                if (w.Location is not LocationCurve lc) continue;
                endpoints[w.Id] = (lc.Curve.GetEndPoint(0), lc.Curve.GetEndPoint(1));
            }

            var ids = endpoints.Keys.ToList();
            var visited = new HashSet<ElementId>();
            var best = new List<ElementId>();

            bool Touch(ElementId a, ElementId b)
            {
                var ea = endpoints[a]; var eb = endpoints[b];
                return ea.A.DistanceTo(eb.A) <= tolFt || ea.A.DistanceTo(eb.B) <= tolFt ||
                       ea.B.DistanceTo(eb.A) <= tolFt || ea.B.DistanceTo(eb.B) <= tolFt;
            }

            foreach (var start in ids)
            {
                if (visited.Contains(start)) continue;
                var comp = new List<ElementId>();
                var q = new Queue<ElementId>();
                q.Enqueue(start); visited.Add(start);
                while (q.Count > 0)
                {
                    var cur = q.Dequeue(); comp.Add(cur);
                    foreach (var other in ids)
                    {
                        if (visited.Contains(other)) continue;
                        if (Touch(cur, other)) { visited.Add(other); q.Enqueue(other); }
                    }
                }
                if (comp.Count > best.Count) best = comp;
            }

            return walls.Where(w => best.Contains(w.Id)).ToList();
        }

        private XYZ PointRU(XYZ O, XYZ R, XYZ U, double z, double r, double u)
        {
            XYZ p = O + R * r + U * u;
            return new XYZ(p.X, p.Y, z);
        }

        private double GetViewPlaneZ(View view)
        {
            try { var sp = view.SketchPlane; if (sp != null) return sp.GetPlane().Origin.Z; }
            catch { }
            return view.Origin.Z;
        }

        private class DimFailurePreprocessor : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
            {
                foreach (var f in fa.GetFailureMessages())
                    if (f.GetSeverity() == FailureSeverity.Warning)
                        fa.DeleteWarning(f);
                return FailureProcessingResult.Continue;
            }
        }
    }
}