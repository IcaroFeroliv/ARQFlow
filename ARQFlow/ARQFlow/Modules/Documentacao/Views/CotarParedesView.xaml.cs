using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using static ARQFlow.Modules.Documentacao.Commands.CotarParedesCommand;

namespace ARQFlow.Modules.Documentacao.Views
{
    public partial class CotarParedesView : Window
    {
        private readonly Document _doc;

        // Propriedade que o Command vai ler após a janela ser fechada
        public CotarParedesParams Parametros { get; private set; }

        // Caminho para salvar o arquivo JSON de configurações na pasta AppData do Windows
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PluginARQ", "CotarParedesConfig.json");

        // Classe auxiliar para preencher o ComboBox com os nomes e IDs das cotas
        private class DimTypeItem
        {
            public string Name { get; set; }
            public ElementId Id { get; set; }
        }

        // Classe auxiliar para serializar/deserializar as configurações no JSON
        private class Config
        {
            public string DimTypeName { get; set; }
            public string Modo { get; set; }
            public double DistanciaCm { get; set; } = 50.0;
            public string Posicao { get; set; }

            // Novos campos do filtro
            public bool IgnorarPequenas { get; set; } = true;
            public double ComprimentoMinimo { get; set; } = 20.0;
        }

        public CotarParedesView(Document doc)
        {
            _doc = doc;
            InitializeComponent();
            CarregarTiposCota();
            CarregarConfig();
        }

        // ═════════════════════════════════════════════════════════════════════
        // CARREGAMENTO DE DADOS DO REVIT
        // ═════════════════════════════════════════════════════════════════════

        private void CarregarTiposCota()
        {
            var tipos = new FilteredElementCollector(_doc)
                .OfClass(typeof(DimensionType))
                .Cast<DimensionType>()
                .Where(dt =>
                {
                    try { return dt.StyleType == DimensionStyleType.Linear; }
                    catch { return false; }
                })
                .OrderBy(dt => dt.Name)
                .Select(dt => new DimTypeItem { Name = dt.Name, Id = dt.Id })
                .ToList();

            CmbDimType.ItemsSource = tipos;

            // Seleciona a primeira cota da lista por padrão, se houver
            if (tipos.Any())
                CmbDimType.SelectedIndex = 0;
        }

        // ═════════════════════════════════════════════════════════════════════
        // PERSISTÊNCIA DE CONFIGURAÇÕES (JSON)
        // ═════════════════════════════════════════════════════════════════════

        private void CarregarConfig()
        {
            if (!File.Exists(ConfigPath)) return;
            try
            {
                var cfg = System.Text.Json.JsonSerializer.Deserialize<Config>(
                              File.ReadAllText(ConfigPath));
                if (cfg == null) return;

                // Restaura o tipo de cota selecionado
                if (!string.IsNullOrEmpty(cfg.DimTypeName))
                {
                    var match = (CmbDimType.ItemsSource as List<DimTypeItem>)
                                    ?.FirstOrDefault(x => x.Name == cfg.DimTypeName);
                    if (match != null) CmbDimType.SelectedItem = match;
                }

                // Restaura o modo de seleção
                if (cfg.Modo == "Manual") RdoSelecionar.IsChecked = true;
                else RdoTodos.IsChecked = true;

                // Restaura a distância e a posição
                TxtDistancia.Text = cfg.DistanciaCm.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (cfg.Posicao == "Interno") RdoInterno.IsChecked = true;
                else RdoExterno.IsChecked = true;

                // Restaura o filtro de paredes pequenas
                ChkIgnorar.IsChecked = cfg.IgnorarPequenas;
                TxtMinimo.Text = cfg.ComprimentoMinimo.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                // Se der erro ao ler o JSON (ex: arquivo corrompido), segue com os valores padrão da UI
            }
        }

        private void SalvarConfig()
        {
            try
            {
                var item = CmbDimType.SelectedItem as DimTypeItem;

                double.TryParse(
                    TxtDistancia.Text.Replace(",", "."),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double dist);

                double.TryParse(
                    TxtMinimo.Text.Replace(",", "."),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double minLen);

                var cfg = new Config
                {
                    DimTypeName = item?.Name,
                    Modo = RdoSelecionar.IsChecked == true ? "Manual" : "Todos",
                    DistanciaCm = dist,
                    Posicao = RdoInterno.IsChecked == true ? "Interno" : "Externo",
                    IgnorarPequenas = ChkIgnorar.IsChecked == true,
                    ComprimentoMinimo = minLen
                };

                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
                File.WriteAllText(ConfigPath, System.Text.Json.JsonSerializer.Serialize(
                    cfg, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        // ═════════════════════════════════════════════════════════════════════
        // EVENTOS DOS BOTÕES
        // ═════════════════════════════════════════════════════════════════════

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnExecutar_Click(object sender, RoutedEventArgs e)
        {
            // Valida se o usuário selecionou uma cota
            if (CmbDimType.SelectedItem == null)
            {
                MessageBox.Show(
                    "Selecione uma família de cota antes de executar.",
                    "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Valida o campo de Distância
            if (!double.TryParse(
                    TxtDistancia.Text.Replace(",", "."),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double dist) || dist < 0)
            {
                MessageBox.Show(
                    "Informe uma distância válida (em centímetros).",
                    "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Valida o campo de Filtro Mínimo (se a caixa estiver marcada)
            double compMinimo = 0;
            if (ChkIgnorar.IsChecked == true)
            {
                if (!double.TryParse(
                        TxtMinimo.Text.Replace(",", "."),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out compMinimo) || compMinimo < 0)
                {
                    MessageBox.Show(
                        "Informe um comprimento mínimo válido (em centímetros) para o filtro.",
                        "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var item = (DimTypeItem)CmbDimType.SelectedItem;

            // Preenche os parâmetros que serão lidos pelo Command
            Parametros = new CotarParedesParams
            {
                DimensionTypeId = item.Id,
                Modo = RdoSelecionar.IsChecked == true
                                        ? CotarModoParedes.SelecionarManual
                                        : CotarModoParedes.TodosVisiveis,
                DistanciaCm = dist,
                Posicao = RdoInterno.IsChecked == true
                                        ? PosicaoParede.Interno
                                        : PosicaoParede.Externo,
                IgnorarPequenas = ChkIgnorar.IsChecked == true,
                ComprimentoMinimoCm = compMinimo
            };

            SalvarConfig();
            DialogResult = true;
            Close();
        }
    }
}