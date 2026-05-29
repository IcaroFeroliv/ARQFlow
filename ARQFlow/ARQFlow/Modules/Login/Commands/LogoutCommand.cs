using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using System;

namespace ARQFlow.Modules.Login.Commands
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class LogoutCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            try
            {
                // 1. Exibe confirmação de logout
                var confirm = new ARQFlow.App.LogoutConfirmWindow();
                var res = confirm.ShowDialog();

                if (res == true)
                {
                    // 2. Usuário confirmou: fazer logout e abrir tela de login novamente
                    ARQFlow.App.AuthController.ForceSignOut();
                    ARQFlow.App.App.SetAuthenticated(false);
                    ARQFlow.App.RibbonBuilder.SetEnabledAllButtons(false);
                    TaskDialog.Show("ARQ Flow", "Logout efetuado. Tela de login será aberta.");

                    // 3. Abrir tela de login para novo login
                    var loginWindow = new ARQFlow.App.LoginWindow();
                    var loginRes = loginWindow.ShowDialog();

                    if (loginRes == true)
                    {
                        // Tenta autenticar com as credenciais fornecidas
                        var success = ARQFlow.App.AuthController.Authenticate(loginWindow.Email, loginWindow.Senha);
                        ARQFlow.App.App.SetAuthenticated(success);
                        ARQFlow.App.RibbonBuilder.SetEnabledAllButtons(success);
                        if (success)
                        {
                            TaskDialog.Show("ARQ Flow", "Login efetuado com sucesso.");
                            return Result.Succeeded;
                        }
                        else
                        {
                            TaskDialog.Show("ARQ Flow", "Falha na autenticação.");
                            return Result.Failed;
                        }
                    }
                    else
                    {
                        // Usuário cancelou a tela de login: manter logout (botões desabilitados)
                        TaskDialog.Show("ARQ Flow", "Login não realizado; botões permanecem desabilitados.");
                        return Result.Cancelled;
                    }
                }
                else
                {
                    // Usuário cancelou a confirmação de logout: sessão permanece ativa
                    TaskDialog.Show("ARQ Flow", "Logout cancelado; sessão inalterada.");
                    return Result.Cancelled;
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("ARQ Flow", "Falha ao tentar logout/login: " + ex.Message);
                return Result.Failed;
            }
        }
    }
}
