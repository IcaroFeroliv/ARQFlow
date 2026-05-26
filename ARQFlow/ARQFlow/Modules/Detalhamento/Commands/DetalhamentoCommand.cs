using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using ARQFlow.Modules.Documentacao.Views;

namespace ARQFlow.Modules.Detalhamento.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class DetalhamentoCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                List<View> viewTemplates = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => v.IsTemplate && v.ViewType == ViewType.Elevation)
                    .OrderBy(v => v.Name)
                    .ToList();

                Views.DetalhamentoWindow window = new Views.DetalhamentoWindow(viewTemplates);
                bool? result = window.ShowDialog();

                if (result != true) return Result.Cancelled;

                if (!window.CriarLado0 && !window.CriarLado1 && !window.CriarLado2 && !window.CriarLado3)
                {
                    TaskDialog.Show("ARQ Flow", "Selecione pelo menos um lado para criar as elevações.");
                    return Result.Cancelled;
                }

                List<Room> ambientesSelecionados = new List<Room>();
                if (window.PegarTodosAmbientes)
                {
                    ambientesSelecionados = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Room>()
                        .Where(r => r.Area > 0)
                        .ToList();
                }
                else
                {
                    try
                    {
                        IList<Reference> refs = uidoc.Selection.PickObjects(ObjectType.Element, new RoomSelectionFilter(), "Selecione os ambientes e clique em Concluir.");
                        foreach (Reference r in refs)
                        {
                            ambientesSelecionados.Add(doc.GetElement(r) as Room);
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
                }

                if (ambientesSelecionados.Count == 0) return Result.Cancelled;

                using (Transaction t = new Transaction(doc, "ARQ Flow: Criar Elevações Vetoriais"))
                {
                    t.Start();

                    ViewFamilyType elevType = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>()
                        .FirstOrDefault(x => x.ViewFamily == ViewFamily.Elevation);

                    if (elevType == null)
                    {
                        TaskDialog.Show("Erro", "Tipo de vista de Elevação não encontrado.");
                        return Result.Failed;
                    }

                    string[] sufixoAlfabetico = { "A", "B", "C", "D" };

                    foreach (Room room in ambientesSelecionados)
                    {
                        LocationPoint locPoint = room.Location as LocationPoint;
                        if (locPoint == null) continue;
                        XYZ center = locPoint.Point;

                        ElevationMarker marker = ElevationMarker.CreateElevationMarker(doc, elevType.Id, center, 50);

                        Dictionary<ViewSection, string> vistasParaManter = new Dictionary<ViewSection, string>();
                        List<ElementId> vistasParaDeletar = new List<ElementId>();

                        for (int i = 0; i < 4; i++)
                        {
                            ViewSection tempView = marker.CreateElevation(doc, doc.ActiveView.Id, i);
                            XYZ dir = tempView.ViewDirection;

                            bool manterEstaVista = false;
                            string direcaoReal = "";

                            if (Math.Abs(dir.Y) > Math.Abs(dir.X))
                            {
                                if (dir.Y > 0)
                                {
                                    direcaoReal = "Sul";
                                    manterEstaVista = window.CriarLado2;
                                }
                                else
                                {
                                    direcaoReal = "Norte";
                                    manterEstaVista = window.CriarLado0;
                                }
                            }
                            else
                            {
                                if (dir.X > 0)
                                {
                                    direcaoReal = "Oeste";
                                    manterEstaVista = window.CriarLado1;
                                }
                                else
                                {
                                    direcaoReal = "Leste";
                                    manterEstaVista = window.CriarLado3;
                                }
                            }

                            if (manterEstaVista)
                            {
                                vistasParaManter.Add(tempView, direcaoReal);
                            }
                            else
                            {
                                vistasParaDeletar.Add(tempView.Id);
                            }
                        }

                        if (vistasParaDeletar.Count > 0)
                        {
                            doc.Delete(vistasParaDeletar);
                        }

                        doc.Regenerate();

                        int vistaCriadaIndex = 0;
                        foreach (var kvp in vistasParaManter)
                        {
                            ViewSection elevView = kvp.Key;
                            string nomeDirecao = kvp.Value;

                            if (window.ModeloVistaSelecionadoId != null && window.ModeloVistaSelecionadoId != ElementId.InvalidElementId)
                            {
                                elevView.ViewTemplateId = window.ModeloVistaSelecionadoId;
                            }

                            // =====================================================================
                            // NOVA CORREÇÃO: MATEMÁTICA SEGURA DA CROP BOX
                            // =====================================================================
                            BoundingBoxXYZ cropBox = elevView.CropBox;
                            if (cropBox != null)
                            {
                                double alturaAmbiente = room.UnboundedHeight;
                                if (alturaAmbiente <= 0) alturaAmbiente = 10.0;

                                // Verifica qual é a altura que o Revit calculou na tela (Max.Y - Min.Y)
                                double alturaAtualDaVista = cropBox.Max.Y - cropBox.Min.Y;

                                // Só altera a caixa de corte se ela ficou esmagada (menor que o ambiente)
                                if (alturaAtualDaVista < alturaAmbiente)
                                {
                                    // Pega a base exata que o Revit achou (Min.Y) e soma a altura do ambiente + folga
                                    XYZ novoMax = new XYZ(cropBox.Max.X, cropBox.Min.Y + alturaAmbiente + 1.0, cropBox.Max.Z);

                                    cropBox.Max = novoMax;
                                    elevView.CropBox = cropBox;
                                }
                            }
                            // =====================================================================

                            string sufixo = window.SufixoDirecao
                                ? nomeDirecao
                                : (window.SufixoAlfabetico ? sufixoAlfabetico[vistaCriadaIndex] : (vistaCriadaIndex + 1).ToString());

                            string novoNome = $"{room.Name} - {sufixo}";
                            elevView.Name = ObterNomeUnico(doc, novoNome);

                            vistaCriadaIndex++;
                        }
                    }

                    t.Commit();
                }

                TaskDialog.Show("ARQ Flow", "Pronto! Elevações geradas e alturas corrigidas com segurança.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Erro", ex.Message);
                return Result.Failed;
            }
        }

        private string ObterNomeUnico(Document doc, string nomeBase)
        {
            string nomeTeste = nomeBase;
            int contador = 1;
            while (new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Any(v => v.Name == nomeTeste))
            {
                nomeTeste = $"{nomeBase} ({contador})";
                contador++;
            }
            return nomeTeste;
        }
    }

    public class RoomSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is Room;
        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}