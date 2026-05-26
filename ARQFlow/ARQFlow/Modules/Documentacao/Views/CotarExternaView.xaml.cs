using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using static ARQFlow.Modules.Documentacao.Commands.CotarExternaCommand;

namespace ARQFlow.Modules.Documentacao.Views
{
    public partial class CotarExternaView : Window
    {
        private readonly Document _doc;

        public CotarExternaParams Parametros { get; private set; }

        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PluginARQ", "CotarExternaConfig.json");

        private class DimTypeItem
        {
            public string Name { get; set; }
            public ElementId Id { get; set; }
        }

        private class Config
        {
            public string DimTypeName { get; set; }
            public string Modo { get; set; }
            public double DistanciaCm { get; set; } = 100.0;
            public bool CotaHorizontal { get; set; } = true;
            public bool CotaVertical { get; set; } = true;
        }

        public CotarExternaView(Document doc)
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
            if (tipos.Any()) CmbDimType.SelectedIndex = 0;
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

                if (!string.IsNullOrEmpty(cfg.DimTypeName))
                {
                    var match = (CmbDimType.ItemsSource as List<DimTypeItem>)
                                    ?.FirstOrDefault(x => x.Name == cfg.DimTypeName);
                    if (match != null) CmbDimType.SelectedItem = match;
                }

                if (cfg.Modo == "Manual") RdoSelecionar.IsChecked = true;
                else RdoTodos.IsChecked = true;

                TxtDistancia.Text = cfg.DistanciaCm.ToString(System.Globalization.CultureInfo.InvariantCulture);
                ChkHorizontal.IsChecked = cfg.CotaHorizontal;
                ChkVertical.IsChecked = cfg.CotaVertical;
            }
            catch { }
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

                var cfg = new Config
                {
                    DimTypeName = item?.Name,
                    Modo = RdoSelecionar.IsChecked == true ? "Manual" : "Todos",
                    DistanciaCm = dist,
                    CotaHorizontal = ChkHorizontal.IsChecked == true,
                    CotaVertical = ChkVertical.IsChecked == true
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
            if (CmbDimType.SelectedItem == null)
            {
                MessageBox.Show("Selecione uma família de cota antes de executar.",
                    "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!double.TryParse(
                    TxtDistancia.Text.Replace(",", "."),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double dist) || dist < 0)
            {
                MessageBox.Show("Informe uma distância válida (em centímetros).",
                    "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (ChkHorizontal.IsChecked != true && ChkVertical.IsChecked != true)
            {
                MessageBox.Show("Selecione ao menos uma cota para inserir.",
                    "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var item = (DimTypeItem)CmbDimType.SelectedItem;

            Parametros = new CotarExternaParams
            {
                DimensionTypeId = item.Id,
                Modo = RdoSelecionar.IsChecked == true
                                      ? CotarModoExterna.SelecionarManual
                                      : CotarModoExterna.TodosVisiveis,
                DistanciaCm = dist,
                CotaHorizontal = ChkHorizontal.IsChecked == true,
                CotaVertical = ChkVertical.IsChecked == true
            };

            SalvarConfig();
            DialogResult = true;
            Close();
        }
    }
}
