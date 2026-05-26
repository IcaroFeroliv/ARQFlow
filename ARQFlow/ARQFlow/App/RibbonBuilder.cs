using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace ARQFlow.App
{
    public static class RibbonBuilder
    {
        public static void Build (UIControlledApplication application)
        {
            // Cria ou acessa a aba
            string tabName = "ARQ Flow";
            try { application.CreateRibbonTab(tabName); } catch { }

            // 1. Cria o painel
            RibbonPanel panel = application.CreateRibbonPanel(tabName, "Modelagem");

            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            string dllFolder = Path.GetDirectoryName(assemblyPath);

            // 1.1. Criar o PulldownButton (O botão pai com a setinha)
            PulldownButtonData pullDownData = new PulldownButtonData(
                "btnRecortes",
                "Auto-Recorte"
            );
            PulldownButton pullDownButton = panel.AddItem(pullDownData) as PulldownButton;

            string mainIconPath = "Modules/Modelagem/Resources/recorte-main.ico";
            SetIcon(pullDownButton, mainIconPath);

            // 1.1.1. Configurar os dados dos Sub-botões
            PushButtonData btnPisoData = new PushButtonData(
                "btnRecortePiso",
                "Pisos",
                assemblyPath,
                "ARQFlow.Modules.Modelagem.Commands.RecortePisoCommand"
            );
            btnPisoData.ToolTip = "Recorta pisos baseado em vínculos de Hidrossanitário.";

            PushButtonData btnParedeData = new PushButtonData(
                "btnRecorteParede",
                "Paredes",
                assemblyPath,
                "ARQFlow.Modules.Modelagem.Commands.RecorteParedeCommand"
            );
            btnParedeData.ToolTip = "Recorta paredes baseado em vínculos de Estrutura.";

            // 1.1.2. Adicionar os sub-botões ao Pulldown
            PushButton btnPiso = pullDownButton.AddPushButton(btnPisoData);
            PushButton btnParede = pullDownButton.AddPushButton(btnParedeData);

            // 1.1.3. Colocar ícones nos sub-botões
            SetIcon(btnPiso, "Modules/Modelagem/Resources/piso-icon.ico");
            SetIcon(btnParede, "Modules/Modelagem/Resources/parede-icon.ico");

            // 2. Novo Painel para Documentação
            RibbonPanel panelDoc = application.CreateRibbonPanel(tabName, "Documentação");

            // 2.1. Criar o botão da UI de Documentação
            PushButtonData btnDocData = new PushButtonData(
                "btnDocAuto",
                "Auto-Tag", // \n quebra a linha no botão
                assemblyPath,
                "ARQFlow.Modules.Documentacao.Commands.DocumentacaoCommand"
            );
            btnDocData.ToolTip = "Abre a interface para inserir, tags e níveis automáticos.";
            PushButton btnDoc = panelDoc.AddItem(btnDocData) as PushButton;

            // 2.1.1. Definir ícone (certifique-se de ter o arquivo .ico na pasta)
            SetIcon(btnDoc, "Modules/Documentacao/Resources/doc-icon.ico");

            // 2.2. Novo Pulldown para Cotas
            PulldownButtonData pulldownButtond = new PulldownButtonData(
                "btnCotas",
                "Cotas"
            );
            PulldownButton pulldDownButtond = panelDoc.AddItem(pulldownButtond) as PulldownButton;

            // 2.2.1. Imagem do botao Pai
            string cotasIconPath = "Modules/Documentacao/Resources/cotasbotaopai.ico";
            SetIcon(pulldDownButtond, cotasIconPath);

            // 2.2.2. Configurar os dados dos Sub-botões
            PushButtonData btnCotasAmbientesData = new PushButtonData(
                "btnCotasAmbientes",
                "Cotar Ambientes",
                assemblyPath,
                "ARQFlow.Modules.Documentacao.Commands.CotarAmbientesCommand"
            );
            btnCotasAmbientesData.ToolTip = "Cota os ambientes na planta baixa.";

            PushButtonData btnCotasParedesData = new PushButtonData(
                "btnCotasParedes",
                "Cotar Paredes",
                assemblyPath,
                "ARQFlow.Modules.Documentacao.Commands.CotarParedesCommand"
            );
            btnCotasParedesData.ToolTip = "Cota as paredes na planta baixa.";

            PushButtonData btnCotaExternaData = new PushButtonData(
                "btnCotaExterna",
                "Cotar Externa",
                assemblyPath,
                "ARQFlow.Modules.Documentacao.Commands.CotarExternaCommand"
            );
            btnCotaExternaData.ToolTip = "Cota a face externa das paredes na planta baixa.";

            // 2.2.3. Adicionar os sub-botões ao Pulldown
            PushButton btnCotasAmbientes = pulldDownButtond.AddPushButton(btnCotasAmbientesData);
            PushButton btnCotasParedes = pulldDownButtond.AddPushButton(btnCotasParedesData);
            PushButton btnCotaExterna = pulldDownButtond.AddPushButton(btnCotaExternaData);

            // 2.2.4. Colocar ícones nos sub-botões
            SetIcon(btnCotasAmbientes, "Modules/Documentacao/Resources/cotasambientes.ico");
            SetIcon(btnCotasParedes, "Modules/Documentacao/Resources/cotasparedes.ico");
            SetIcon(btnCotaExterna, "Modules/Documentacao/Resources/cotaexterna.ico");

            //3. Novo Painel para Detalhamento
            RibbonPanel panelDetalhamento = application.CreateRibbonPanel(tabName, "Detalhamento");

            //3.1. Criar o botão da UI de Detalhamento
            PushButtonData btnDetalhamentoData = new PushButtonData(
                "btnDetalhamentoAuto",
                "Auto-Detalhe",
                assemblyPath,
                "ARQFlow.Modules.Detalhamento.Commands.DetalhamentoCommand"
            );
            btnDetalhamentoData.ToolTip = "Criar elevações através de um ambiente";
            PushButton btnDetalhamento = panelDetalhamento.AddItem(btnDetalhamentoData) as PushButton;

            //3.1.1. Definir ícone 
            SetIcon(btnDetalhamento, "Modules/Detalhamento/Resources/detalhamento.ico");

        }

        /*private static void SetIcon(RibbonButton button, string iconPath)
        {
            if (!File.Exists(iconPath))
                return;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            button.LargeImage = bitmap; // Ícone grande (32x32)
            button.Image = bitmap;      // Ícone pequeno (16x16)
        }*/
        private static void SetIcon(RibbonButton button, string resourcePath)
        {
            try
            {
                // Descobre dinamicamente o nome do seu assembly (ARQFlow)
                string assemblyName = Assembly.GetExecutingAssembly().GetName().Name;

                // Monta o endereço interno do recurso embutido na DLL
                string uriPath = $"pack://application:,,,/{assemblyName};component/{resourcePath}";

                var bitmap = new BitmapImage(new Uri(uriPath, UriKind.Absolute));
                button.LargeImage = bitmap; // 32x32
                button.Image = bitmap;      // 16x16
            }
            catch (Exception)
            {
                TaskDialog.Show("Erro", $"Não foi possível carregar o ícone: {resourcePath}");
            }
        }
    }
}
