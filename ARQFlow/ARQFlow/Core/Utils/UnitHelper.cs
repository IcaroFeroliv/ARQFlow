using Autodesk.Revit.DB;

namespace ARQFlow.Core.Utils
{
    public static class UnitHelper
    {
        // 1 foot = 30.48 cm (exato por definição internacional)
        private const double CM_PER_FOOT = 30.48;
        private const double M3_PER_FOOT3 = 0.0283168; // 1 pé³ = 0,0283168 m³

        /// <summary>Converte centímetros para pés (unidade interna do Revit).</summary>
        public static double CmToFeet(double cm) => cm / CM_PER_FOOT;

        /// <summary>Converte metros para pés.</summary>
        public static double MToFeet(double m) => m * 100.0 / CM_PER_FOOT;

        /// <summary>Converte metros cúbicos para pés cúbicos.</summary>
        public static double M3ToFeet3(double m3) => m3 / M3_PER_FOOT3;

        /// <summary>Converte pés cúbicos para metros cúbicos (para debug/log).</summary>
        public static double Feet3ToM3(double feet3) => feet3 * M3_PER_FOOT3;

        /// <summary>Converte pés para centímetros (para exibição/debug).</summary>
        public static double FeetToCm(double feet) => feet * CM_PER_FOOT;

    }
}