using Autodesk.Revit.UI;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ARQFlow.App
{
    public class AuthController
    {
        private static readonly HttpClient Client = new HttpClient();
        private static readonly string ApiPath = "http://192.168.1.160:8080/api/AuthControllerARQFlow/";
        public static string AccessToken { get; private set; }
        

        // Ao iniciar: se houver token em memória, valida primeiro.
        // Se inválido ou ausente, retorna false (deve pedir login).
        public static bool ChamarLogin() {
            var loginWindow = new LoginWindow();
            var result = loginWindow.ShowDialog();
            var IsAuthenticated = false;
            if (result == true)
            {
                IsAuthenticated = AuthController.Authenticate(loginWindow.Email, loginWindow.Senha);
                if (!IsAuthenticated)
                {
                    Task.Run(() => System.Windows.MessageBox.Show("Falha na autenticação. Verifique suas credenciais e tente novamente."));
                }
            }
            return IsAuthenticated;
        }


        public static bool Authenticate()
        {
            try
            {
                // 1) Se já temos access token em memória, valide-o
                if (!string.IsNullOrWhiteSpace(AccessToken))
                {
                    if (ValidateCurrentUser())
                    {
                        return true;
                    }

                    return false;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // Login com email/senha; espera que o servidor retorne accessToken
        public static bool Authenticate(string email, string password)
        {
            try
            {
                var content = new StringContent(
                    JsonSerializer.Serialize(new { email, password }),
                    Encoding.UTF8,
                    "application/json");

                var loginUrl = ApiPath + "login";
                var response = Client.PostAsync(loginUrl, content).Result;
                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                var responseBody = response.Content.ReadAsStringAsync().Result;

                var accessToken = ExtractToken(responseBody);
                var expires = ExtractExpires(responseBody);

                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    return false;
                }

                AccessToken = accessToken;
                

                if (!ValidateCurrentUser())
                {
                    SignOut();
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        // Retorna o access token atual; tentará renovar se ausente/expirado
        public static string GetAccessToken()
        {
            try
            {
                return AccessToken;
            }
            catch
            {
                return null;
            }
        }

        public static void SignOut()
        {
            try
            {
                AccessToken = null;
            }
            catch
            {
            }
        }

        // Valida permissão do usuário atual via GET /validar
        private static bool ValidateCurrentUser()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(AccessToken))
                {
                    return false;
                }

                var validarUrl = ApiPath + "validar";
                using var request = new HttpRequestMessage(HttpMethod.Get, validarUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
                var response = Client.SendAsync(request).Result;
                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ExtractToken(string responseBody)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                if (root.TryGetProperty("token", out var tokenElement))
                {
                    return tokenElement.GetString();
                }
                if (root.TryGetProperty("accessToken", out var accessTokenElement))
                {
                    return accessTokenElement.GetString();
                }
                if (root.TryGetProperty("idToken", out var idTokenElement))
                {
                    return idTokenElement.GetString();
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static int ExtractExpires(string responseBody)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return 0;
                }

                if (root.TryGetProperty("expiresInSeconds", out var e) && e.ValueKind == JsonValueKind.Number)
                {
                    var seconds = e.GetInt32();
                    return seconds;
                }
                if (root.TryGetProperty("expiresIn", out var e2) && e2.ValueKind == JsonValueKind.Number)
                {
                    var seconds = e2.GetInt32();
                    return seconds;
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        

    }
}
