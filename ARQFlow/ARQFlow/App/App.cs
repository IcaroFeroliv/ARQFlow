using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.UI;
using System.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;

namespace ARQFlow.App
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication aplication)
        {
            try
            {
                /* 
                 - Sistema de Atualização Automática 
                 - Sistema de Login e autenticação
                */
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
