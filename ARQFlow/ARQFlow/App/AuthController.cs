using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ARQFlow.App
{
    public class AuthController
    {
        private static readonly HttpClient Client = new HttpClient();
        private static readonly string ApiPath = "http://192.168.1.160:8080/api/AuthControllerARQFlow/";
        private static readonly string AccessTokenFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ARQFlow",
            "access.token");
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

                // 2) Tenta carregar token persistido
                var persisted = LoadAccessTokenFromDisk();
                if (string.IsNullOrWhiteSpace(persisted)) return false;
                AccessToken = persisted;
                return ValidateCurrentUser();
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

                // Persistir token seguro no disco
                try { SaveAccessTokenToDisk(AccessToken); } catch { }

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

        // Exibe diálogo de confirmação e, se confirmado, remove o token. Retorna true se o logout foi realizado.
        public static bool SignOut()
        {
            try
            {
                // Exibe confirmação robusta para evitar logout acidental
                var confirm = new LogoutConfirmWindow();
                var res = confirm.ShowDialog();
                if (res == true)
                {
                    AccessToken = null;
                    try { DeletePersistedAccessToken(); } catch { }
                    return true;
                }
                return false;
            }
            catch
            {}
            return false;
        }

        // Força sign-out sem pedir confirmação (usado quando o usuário fecha a tela de login)
        public static void ForceSignOut()
        {
            try
            {
                AccessToken = null;
                try { DeletePersistedAccessToken(); } catch { }
            }
            catch { }
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
        // Persistência segura do access token usando DPAPI (CurrentUser)
        private static void SaveAccessTokenToDisk(string token)
        {
            try
            {
                var directory = Path.GetDirectoryName(AccessTokenFilePath);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var protectedBytes = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(token),
                    optionalEntropy: null,
                    scope: DataProtectionScope.CurrentUser);

                File.WriteAllBytes(AccessTokenFilePath, protectedBytes);
            }
            catch
            {
                // best-effort
            }
        }

        private static string LoadAccessTokenFromDisk()
        {
            try
            {
                if (!File.Exists(AccessTokenFilePath)) return null;

                var protectedBytes = File.ReadAllBytes(AccessTokenFilePath);
                var bytes = ProtectedData.Unprotect(
                    protectedBytes,
                    optionalEntropy: null,
                    scope: DataProtectionScope.CurrentUser);

                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return null;
            }
        }

        private static void DeletePersistedAccessToken()
        {
            try
            {
                if (File.Exists(AccessTokenFilePath)) File.Delete(AccessTokenFilePath);
            }
            catch
            {
            }
        }

    }
}
