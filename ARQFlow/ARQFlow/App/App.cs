using System;
using Autodesk.Revit.UI;
using System.Threading.Tasks;
namespace ARQFlow.App
{
    public class App : IExternalApplication
    {
        private static bool _isAuthenticated;
        public static bool IsAuthenticated => _isAuthenticated;

        public static void SetAuthenticated(bool value)
        {
            _isAuthenticated = value;
        }

        public Result OnStartup(UIControlledApplication aplication)
        {
            try
            { 
                _isAuthenticated = AuthController.Authenticate();
                if (!_isAuthenticated) _isAuthenticated = AuthController.ChamarLogin();
                RibbonBuilder.Build(aplication, _isAuthenticated);
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
