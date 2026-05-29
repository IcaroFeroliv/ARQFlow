using System;
using Autodesk.Revit.UI;
using System.Threading.Tasks;
namespace ARQFlow.App
{
    public class App : IExternalApplication
    {
        public static bool IsAuthenticated { get; private set; }
        public Result OnStartup(UIControlledApplication aplication)
        {
            try
            {

                IsAuthenticated = AuthController.Authenticate();
                if (!IsAuthenticated) IsAuthenticated = AuthController.ChamarLogin();
                RibbonBuilder.Build(aplication);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                return Result.Failed;
            }
        }
       public Result OnShutdown(UIControlledApplication application)
       {
            return Result.Succeeded;
       }
    }
}
