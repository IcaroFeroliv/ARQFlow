using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace ARQFlow.Modules.Documentacao.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class DocumentacaoCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // 1. Instanciar sua Janela (UI)
            // Aqui vamos criar o WPF depois
            var minhaJanela = new ARQFlow.Modules.Documentacao.Views.DocumentacaoWindow(commandData);
            minhaJanela.ShowDialog();

            return Result.Succeeded;
        }
    }
}