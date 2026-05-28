using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using ARQFlow.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ARQFlow.Modules.Modelagem.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RecortePisoCommand : IExternalCommand
    {
        private const bool DEBUG = false;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var links = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>().ToList();

            if (!links.Any()) { TaskDialog.Show("Erro", "Nenhum vinculo encontrado."); return Result.Cancelled; }

            var janela = new Modules.Modelagem.Views.RecortePisoView(links);
            if (janela.ShowDialog() != true) return Result.Cancelled;

            Document linkDoc = janela.LinkSelecionado.GetLinkDocument();
            Transform linkTransform = janela.LinkSelecionado.GetTotalTransform();
            double folgaFeet = UnitHelper.CmToFeet(janela.Folga);

            // PASSO 1: seleciona o piso alvo
            Floor pisoAlvo = null;
            try
            {
                TaskDialog.Show("Passo 1 de 2", "Selecione o PISO (laje) onde os furos serao criados.\nClique OK e depois clique no piso na vista.");
                Reference refPiso = uidoc.Selection.PickObject(ObjectType.Element,
                    new FloorSelectionFilter(), "Selecione o piso alvo");
                pisoAlvo = doc.GetElement(refPiso.ElementId) as Floor;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

            if (pisoAlvo == null) { TaskDialog.Show("Erro", "Elemento selecionado nao e um piso."); return Result.Cancelled; }

            double zTopoPiso = ObterZTopoPisoFace(doc, pisoAlvo);
            double zBasePiso = ObterZBasePisoFace(doc, pisoAlvo);
            double espessura = zTopoPiso - zBasePiso;

            if (espessura > UnitHelper.CmToFeet(80.0))
            {
                TaskDialog.Show("Aviso",
                    $"Piso com espessura {UnitHelper.FeetToCm(espessura):F0}cm — parece ser terreno.\n" +
                    "Selecione a laje estrutural correta.");
                return Result.Cancelled;
            }

            // PASSO 2: modo de operacao
            TaskDialog dlg = new TaskDialog("Passo 2 de 2");
            dlg.MainInstruction = $"Piso ID {pisoAlvo.Id} | Espessura: {UnitHelper.FeetToCm(espessura):F1}cm\nComo deseja realizar os furos?";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Selecionar elementos manualmente");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Processar todo o piso automaticamente");
            TaskDialogResult tResult = dlg.Show();

            if (tResult == TaskDialogResult.CommandLink1)
            {
                var elemsSelecionados = new List<Element>();
                try
                {
                    IList<Reference> refs = uidoc.Selection.PickObjects(
                        ObjectType.LinkedElement,
                        "Selecione as pecas no vinculo (ENTER para confirmar)");
                    foreach (Reference r in refs)
                        elemsSelecionados.Add(linkDoc.GetElement(r.LinkedElementId));
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    if (!elemsSelecionados.Any()) return Result.Cancelled;
                }

                if (!elemsSelecionados.Any()) { TaskDialog.Show("Aviso", "Nenhum elemento selecionado."); return Result.Cancelled; }

                int furosM = 0, errosM = 0;
                using (Transaction t = new Transaction(doc, "Furos - Elementos Selecionados"))
                {
                    t.Start(); AplicarFHO(t);
                    foreach (Element el in elemsSelecionados)
                        try { if (ProcessarElemento(doc, el, linkTransform, folgaFeet, pisoAlvo, zTopoPiso, zBasePiso)) furosM++; }
                        catch { errosM++; }
                    t.Commit();
                }
                TaskDialog.Show("Resultado",
                    $"Elementos selecionados: {elemsSelecionados.Count}\n" +
                    $"Recortes criados: {furosM}\n" +
                    $"Sem intersecao/erros: {errosM}");
            }
            else
            {
                var cats = new List<BuiltInCategory>
                {
                    BuiltInCategory.OST_GenericModel,
                    BuiltInCategory.OST_PlumbingFixtures,
                    BuiltInCategory.OST_MechanicalEquipment,
                    BuiltInCategory.OST_Sprinklers,
                };
                var elems = new List<Element>();
                foreach (var cat in cats)
                    elems.AddRange(new FilteredElementCollector(linkDoc)
                        .OfCategory(cat).WhereElementIsNotElementType().ToList());

                if (!elems.Any()) { TaskDialog.Show("Aviso", "Nenhum elemento encontrado."); return Result.Cancelled; }

                int furos = 0, erros = 0;
                using (Transaction t = new Transaction(doc, "Recortes em Lote - Piso"))
                {
                    t.Start(); AplicarFHO(t);
                    foreach (Element el in elems)
                        try { if (ProcessarElemento(doc, el, linkTransform, folgaFeet, pisoAlvo, zTopoPiso, zBasePiso)) furos++; }
                        catch { erros++; }
                    t.Commit();
                }
                TaskDialog.Show("Concluido", $"Piso: ID {pisoAlvo.Id}\nRecortes criados: {furos}\nSem intersecao/erros: {erros}");
            }

            return Result.Succeeded;
        }

        // =====================================================================
        // PROCESSAMENTO
        // Usa o transform da instancia para criar o furo ORIENTADO com a familia,
        // resolvendo o problema de elementos rotacionados.
        // =====================================================================
        private bool ProcessarElemento(Document doc, Element el, Transform linkTransform,
                                        double folga, Floor pisoAlvo,
                                        double zTopoPiso, double zBasePiso)
        {
            double margem = UnitHelper.CmToFeet(2.0);

            // Verifica Z do elemento via BBox (comprovado correto)
            BoundingBoxXYZ bbTotal = el.get_BoundingBox(null);
            if (bbTotal == null) return false;
            var cantosTotal = ObterCantosTransformados(bbTotal, linkTransform);
            double zElemMin = cantosTotal.Min(p => p.Z);
            double zElemMax = cantosTotal.Max(p => p.Z);
            if (zElemMin > zTopoPiso + margem) return false;
            if (zElemMax < zBasePiso - margem) return false;

            // ── ORIENTACAO DA FAMILIA ─────────────────────────────────────────
            // O transform da GeometryInstance contem a rotacao da familia
            // em coords do link. Combinando com linkTransform obtemos
            // os eixos locais da familia em coords do host.
            // Assim o furo fica alinhado com a familia — nao com os eixos globais.
            var geoOpt = new Options { DetailLevel = ViewDetailLevel.Fine };
            var geo = el.get_Geometry(geoOpt);

            Transform tfFamilia = null; // transform da primeira GeometryInstance (define orientacao)
            double larguraSimbolo = 0, comprimentoSimbolo = 0; // dimensoes em coords locais

            if (geo != null)
            {
                foreach (GeometryObject obj in geo)
                {
                    if (!(obj is GeometryInstance gi)) continue;

                    // Transform combinado: link + instancia = orientacao em coords host
                    Transform tfHost = linkTransform.Multiply(gi.Transform);

                    // Pega dimensoes do maior solido em coords do SIMBOLO (nao transformado)
                    // Isso garante que as dimensoes sao as do simbolo, sem distorcao do transform
                    Solid solidoPrincipal = null;
                    double volMax = 0;
                    foreach (GeometryObject o in gi.GetSymbolGeometry())
                    {
                        if (!(o is Solid s) || s.Volume <= volMax) continue;
                        volMax = s.Volume;
                        solidoPrincipal = s;
                    }

                    if (solidoPrincipal == null) continue;

                    // BBox do solido em coords do simbolo — sem rotacao
                    BoundingBoxXYZ bbSym = solidoPrincipal.GetBoundingBox();
                    larguraSimbolo = bbSym.Max.X - bbSym.Min.X;   // dimensao X local
                    comprimentoSimbolo = bbSym.Max.Y - bbSym.Min.Y; // dimensao Y local
                    double halfX = larguraSimbolo / 2.0 + folga;
                    double halfY = comprimentoSimbolo / 2.0 + folga;

                    if (larguraSimbolo < UnitHelper.CmToFeet(2.0)) continue;
                    if (comprimentoSimbolo < UnitHelper.CmToFeet(2.0)) continue;

                    // Eixos locais da familia em coords do host
                    XYZ eixoX = tfHost.OfVector(XYZ.BasisX).Normalize();
                    XYZ eixoY = tfHost.OfVector(XYZ.BasisY).Normalize();

                    // Sempre usa retangulo ORIENTADO com os eixos da familia.
                    // Funciona para qualquer angulo — inclusive 0° e 90°.
                    // O centro e calculado a partir do BBox do simbolo transformado.
                    XYZ centroSimbolo = new XYZ(
                        (bbSym.Min.X + bbSym.Max.X) / 2.0,
                        (bbSym.Min.Y + bbSym.Max.Y) / 2.0,
                        0);
                    XYZ centroHost = tfHost.OfPoint(centroSimbolo);
                    centroHost = new XYZ(centroHost.X, centroHost.Y, zTopoPiso);

                    bool rotacionado = Math.Abs(eixoX.X) < 0.999 && Math.Abs(eixoX.Y) < 0.999;

                    XYZ p1 = new XYZ((centroHost - halfX * eixoX - halfY * eixoY).X, (centroHost - halfX * eixoX - halfY * eixoY).Y, zTopoPiso);
                    XYZ p2 = new XYZ((centroHost + halfX * eixoX - halfY * eixoY).X, (centroHost + halfX * eixoX - halfY * eixoY).Y, zTopoPiso);
                    XYZ p3 = new XYZ((centroHost + halfX * eixoX + halfY * eixoY).X, (centroHost + halfX * eixoX + halfY * eixoY).Y, zTopoPiso);
                    XYZ p4 = new XYZ((centroHost - halfX * eixoX + halfY * eixoY).X, (centroHost - halfX * eixoX + halfY * eixoY).Y, zTopoPiso);

                    if (DEBUG)
                    {
                        string dbg = $"Familia: {(el as FamilyInstance)?.Symbol?.FamilyName}\n";
                        dbg += $"Rotacionado: {rotacionado}\n";
                        dbg += $"EixoX: ({eixoX.X:F3}, {eixoX.Y:F3})\n";
                        dbg += $"EixoY: ({eixoY.X:F3}, {eixoY.Y:F3})\n\n";
                        dbg += $"Maior solido (vol={volMax:F3}):\n";
                        dbg += $"  X simbolo: {UnitHelper.FeetToCm(larguraSimbolo):F1} cm\n";
                        dbg += $"  Y simbolo: {UnitHelper.FeetToCm(comprimentoSimbolo):F1} cm\n\n";
                        int idx2 = 0;
                        foreach (GeometryObject o2 in gi.GetSymbolGeometry())
                        {
                            if (!(o2 is Solid s2) || s2.Volume < 1e-6) continue;
                            BoundingBoxXYZ bb2 = s2.GetBoundingBox();
                            dbg += $"  sol{idx2++}: vol={s2.Volume:F3} X={UnitHelper.FeetToCm(bb2.Max.X - bb2.Min.X):F1} Y={UnitHelper.FeetToCm(bb2.Max.Y - bb2.Min.Y):F1}\n";
                        }
                        TaskDialog.Show("DEBUG FURO", dbg);
                    }

                    CurveArray contorno = new CurveArray();
                    contorno.Append(Line.CreateBound(p1, p2));
                    contorno.Append(Line.CreateBound(p2, p3));
                    contorno.Append(Line.CreateBound(p3, p4));
                    contorno.Append(Line.CreateBound(p4, p1));

                    using (SubTransaction st = new SubTransaction(doc))
                    {
                        st.Start();
                        try { doc.Create.NewOpening(pisoAlvo, contorno, true); st.Commit(); return true; }
                        catch { st.RollBack(); }
                    }

                    break; // usa apenas a primeira GeometryInstance
                }
            }

            return false;
        }

        private double ObterZTopoPisoFace(Document doc, Floor piso)
        {
            var opt = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false };
            double zMax = double.MinValue;
            foreach (GeometryObject obj in piso.get_Geometry(opt) ?? Enumerable.Empty<GeometryObject>())
            {
                if (!(obj is Solid s) || s.Volume < 1e-6) continue;
                foreach (Face f in s.Faces)
                {
                    BoundingBoxUV bb = f.GetBoundingBox();
                    UV ct = new UV((bb.Min.U + bb.Max.U) / 2.0, (bb.Min.V + bb.Max.V) / 2.0);
                    if (f.ComputeNormal(ct).Z > 0.7) { double z = f.Evaluate(ct).Z; if (z > zMax) zMax = z; }
                }
            }
            return zMax > double.MinValue ? zMax : (piso.get_BoundingBox(null)?.Max.Z ?? 0.0);
        }

        private double ObterZBasePisoFace(Document doc, Floor piso)
        {
            var opt = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false };
            double zMin = double.MaxValue;
            foreach (GeometryObject obj in piso.get_Geometry(opt) ?? Enumerable.Empty<GeometryObject>())
            {
                if (!(obj is Solid s) || s.Volume < 1e-6) continue;
                foreach (Face f in s.Faces)
                {
                    BoundingBoxUV bb = f.GetBoundingBox();
                    UV ct = new UV((bb.Min.U + bb.Max.U) / 2.0, (bb.Min.V + bb.Max.V) / 2.0);
                    if (f.ComputeNormal(ct).Z < -0.7) { double z = f.Evaluate(ct).Z; if (z < zMin) zMin = z; }
                }
            }
            return zMin < double.MaxValue ? zMin : (piso.get_BoundingBox(null)?.Min.Z ?? 0.0);
        }

        private List<XYZ> ObterCantosTransformados(BoundingBoxXYZ bb, Transform tf)
        {
            XYZ mn = bb.Min, mx = bb.Max;
            return new List<XYZ>
            {
                tf.OfPoint(new XYZ(mn.X, mn.Y, mn.Z)), tf.OfPoint(new XYZ(mx.X, mn.Y, mn.Z)),
                tf.OfPoint(new XYZ(mx.X, mx.Y, mn.Z)), tf.OfPoint(new XYZ(mn.X, mx.Y, mn.Z)),
                tf.OfPoint(new XYZ(mn.X, mn.Y, mx.Z)), tf.OfPoint(new XYZ(mx.X, mn.Y, mx.Z)),
                tf.OfPoint(new XYZ(mx.X, mx.Y, mx.Z)), tf.OfPoint(new XYZ(mn.X, mx.Y, mx.Z)),
            };
        }

        private void AplicarFHO(Transaction t)
        {
            FailureHandlingOptions fho = t.GetFailureHandlingOptions();
            fho.SetFailuresPreprocessor(new SilentFailureHandler());
            t.SetFailureHandlingOptions(fho);
        }

        public class FloorSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is Floor;
            public bool AllowReference(Reference reference, XYZ position) => true;
        }

        public class SilentFailureHandler : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
            {
                foreach (FailureMessageAccessor f in fa.GetFailureMessages())
                    if (f.GetSeverity() == FailureSeverity.Warning) fa.DeleteWarning(f);
                return FailureProcessingResult.Continue;
            }
        }
    }
}