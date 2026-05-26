using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using static ARQFlow.Modules.Documentacao.Commands.CotarAmbientesCommand;

namespace ARQFlow.Modules.Documentacao.Views
{
    public partial class CotarAmbientesView : Window
    {
        private readonly Document _doc;

        public CotarAmbientesParams Parametros { get; private set; }

        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ARQFlow", "CotarAmbientesConfig.json");

        private class DimTypeItem
        {
            public string Name { get; set; }
            public ElementId Id { get; set; }
        }

        private class Config
        {
            public string DimTypeName { get; set; }
            public string Modo { get; set; }
            public double DistanciaCm { get; set; } = 30.0;
            public string PosicaoH { get; set; }
            public string PosicaoV { get; set; }
        }

        public CotarAmbientesView(Document doc)
        {
            _doc = doc;
            InitializeComponent();
            CarregarTiposCota();
            CarregarConfig();
        }

        // ═════════════════════════════════════════════════════════════════════
        // CARREGAMENTO
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
            if (tipos.Any())
                CmbDimType.SelectedIndex = 0;
        }

        // ═════════════════════════════════════════════════════════════════════
        // PERSISTÊNCIA
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

                TxtDistancia.Text = cfg.DistanciaCm
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);

                if (cfg.PosicaoH == "Acima") RdoHAcima.IsChecked = true;
                else RdoHAbaixo.IsChecked = true;

                if (cfg.PosicaoV == "Direita") RdoVDireita.IsChecked = true;
                else RdoVEsquerda.IsChecked = true;
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
                    PosicaoH = RdoHAcima.IsChecked == true ? "Acima" : "Abaixo",
                    PosicaoV = RdoVDireita.IsChecked == true ? "Direita" : "Esquerda",
                };

                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
                File.WriteAllText(ConfigPath, System.Text.Json.JsonSerializer.Serialize(
                    cfg, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        // ═════════════════════════════════════════════════════════════════════
        // EVENTOS
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
                MessageBox.Show(
                    "Selecione uma família de cota antes de executar.",
                    "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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

            var item = (DimTypeItem)CmbDimType.SelectedItem;

            Parametros = new CotarAmbientesParams
            {
                DimensionTypeId = item.Id,
                Modo = RdoSelecionar.IsChecked == true
                                    ? CotarModo.SelecionarManual
                                    : CotarModo.TodosVisiveis,
                DistanciaCm = dist,
                PosicaoH = RdoHAcima.IsChecked == true
                                    ? PosicaoHorizontal.Acima
                                    : PosicaoHorizontal.Abaixo,
                PosicaoV = RdoVDireita.IsChecked == true
                                    ? PosicaoVertical.Direita
                                    : PosicaoVertical.Esquerda,
            };

            SalvarConfig();
            DialogResult = true;
            Close();
        }
    }
}
