using System;
using System.Globalization;
using System.Text;

namespace Skunk.Units
{
    /// <summary>Length formats, named like the Project Units / type "Units Format" options.</summary>
    public enum LengthFormat
    {
        /// <summary>Rhino document units: Document Properties > Units (unit system, distance display, precision).</summary>
        Project = 0,
        FeetFractionalInches,
        DecimalFeet,
        FeetDecimalInches,
        FractionalInches,
        DecimalInches,
        Meters,
        Centimeters,
        Millimeters,
    }

    /// <summary>How paper sizes (text height, symbol size, arrow length) are shown and typed: Rhino's page units.</summary>
    public enum PaperUnit
    {
        Millimeters = 0,
        FractionalInches,
        DecimalInches,
    }

    /// <summary>Slope formats ("Slope" units).</summary>
    public enum SlopeFormat
    {
        Percent = 0,
        PerMille,
        DecimalDegrees,
        DegreesMinutes,
        RiseOver12Inches,
        RiseOver1Foot,
        RiseOver120Inches,
        RiseOver1000Millimeters,
        RiseOver10Feet,
        RatioOneToX,
        RatioXTo12,
        Ratio12ToX,
        RatioXTo10,
        RatioXToOne,
        Ratio10ToX,
        RiseOverRun,
    }

    /// <summary>Display settings for a length value (the "Format" dialog).</summary>
    public sealed class LengthStyle
    {
        public LengthFormat Format = LengthFormat.Project;
        /// <summary>Decimal places for decimal formats.</summary>
        public int Precision = 2;
        /// <summary>Denominator of the rounding fraction for fractional inch formats (2..256).</summary>
        public int FractionDenominator = 8;
        public bool ShowUnitSymbol = true;
        public bool SuppressTrailingZeros = false;
        public bool SuppressLeadingZeros = false;
        public bool SuppressZeroFeet = false;
        public bool SuppressZeroInches = false;
        public bool ShowPlusForPositive = false;
        public bool UseDigitGrouping = false;
        /// <summary>true: 10'-6"; false: 10' - 6" (the usual default has spaces).</summary>
        public bool SuppressSpaces = true;

        public LengthStyle Clone() => (LengthStyle)MemberwiseClone();

        /// <summary>Imperial architectural default: feet and fractional inches to 1/8".</summary>
        public static LengthStyle Imperial() => new LengthStyle { Format = LengthFormat.FeetFractionalInches, FractionDenominator = 8 };

        /// <summary>Metric default: millimetres, no decimals.</summary>
        public static LengthStyle Metric() => new LengthStyle { Format = LengthFormat.Millimeters, Precision = 0, ShowUnitSymbol = false };
    }

    /// <summary>Display settings for a slope value.</summary>
    public sealed class SlopeStyle
    {
        public SlopeFormat Format = SlopeFormat.Percent;
        public int Precision = 2;
        /// <summary>Denominator for the fractional rise of Rise / 12" style formats.</summary>
        public int FractionDenominator = 16;
        public bool ShowUnitSymbol = true;
        public bool SuppressTrailingZeros = false;

        public SlopeStyle Clone() => (SlopeStyle)MemberwiseClone();
    }

    /// <summary>
    /// Rhino-free formatting of lengths, slopes and angles, following common architectural conventions
    /// (feet-inches with fractions, digit grouping, suppressing zero feet / inches, + for positive values).
    /// All inputs are in metres (lengths), rise/run ratios (slopes) or degrees (angles).
    /// </summary>
    public static class UnitFormat
    {
        public const double MetersPerFoot = 0.3048;
        public const double MetersPerInch = 0.0254;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Format a length given in metres.</summary>
        public static string Length(double meters, LengthStyle style)
        {
            if (style == null)
                style = LengthStyle.Imperial();
            if (double.IsNaN(meters) || double.IsInfinity(meters))
                return "?";
            switch (style.Format)
            {
                case LengthFormat.FeetFractionalInches:
                    return Signed(meters, style, FeetFractionalInches(Math.Abs(meters), style));
                case LengthFormat.FeetDecimalInches:
                    return Signed(meters, style, FeetDecimalInches(Math.Abs(meters), style));
                case LengthFormat.FractionalInches:
                    return Signed(meters, style, FractionalInches(Math.Abs(meters) / MetersPerInch, style.FractionDenominator, style.UseDigitGrouping) + (style.ShowUnitSymbol ? "\"" : ""));
                case LengthFormat.DecimalFeet:
                    return Signed(meters, style, Decimal(Math.Abs(meters) / MetersPerFoot, style) + (style.ShowUnitSymbol ? "'" : ""));
                case LengthFormat.DecimalInches:
                    return Signed(meters, style, Decimal(Math.Abs(meters) / MetersPerInch, style) + (style.ShowUnitSymbol ? "\"" : ""));
                case LengthFormat.Centimeters:
                    return Signed(meters, style, Decimal(Math.Abs(meters) * 100.0, style) + (style.ShowUnitSymbol ? " cm" : ""));
                case LengthFormat.Millimeters:
                    return Signed(meters, style, Decimal(Math.Abs(meters) * 1000.0, style) + (style.ShowUnitSymbol ? " mm" : ""));
                case LengthFormat.Meters:
                case LengthFormat.Project:
                default:
                    return Signed(meters, style, Decimal(Math.Abs(meters), style) + (style.ShowUnitSymbol ? " m" : ""));
            }
        }

        static string Signed(double value, LengthStyle style, string magnitude)
        {
            if (IsZeroText(magnitude))
                return magnitude;
            if (value < 0)
                return "-" + magnitude;
            if (style.ShowPlusForPositive && value > 0)
                return "+" + magnitude;
            return magnitude;
        }

        static bool IsZeroText(string text)
        {
            foreach (char c in text)
                if (c >= '1' && c <= '9')
                    return false;
            return true;
        }

        static string FeetFractionalInches(double meters, LengthStyle s)
        {
            int den = NormalizeDenominator(s.FractionDenominator);
            double inches = meters / MetersPerInch;
            long units = (long)Math.Round(inches * den, MidpointRounding.AwayFromZero);
            long perFoot = 12L * den;
            long feet = units / perFoot;
            long rem = units % perFoot;
            long wholeIn = rem / den;
            long num = rem % den;
            long d = den;
            Reduce(ref num, ref d);

            string inchPart = wholeIn.ToString(Inv);
            if (num != 0)
                inchPart += " " + num.ToString(Inv) + "/" + d.ToString(Inv);
            string feetText = Group(feet, s.UseDigitGrouping) + "'";
            string sep = s.SuppressSpaces ? "-" : " - ";
            bool zeroInches = wholeIn == 0 && num == 0;
            if (feet == 0 && s.SuppressZeroFeet)
                return (wholeIn == 0 && num != 0 ? num.ToString(Inv) + "/" + d.ToString(Inv) : inchPart) + "\"";
            if (zeroInches && s.SuppressZeroInches)
                return feetText;
            return feetText + sep + inchPart + "\"";
        }

        static string FeetDecimalInches(double meters, LengthStyle s)
        {
            double inches = meters / MetersPerInch;
            double factor = Math.Pow(10, Math.Max(0, s.Precision));
            double rounded = Math.Round(inches * factor, MidpointRounding.AwayFromZero) / factor;
            long feet = (long)Math.Floor(rounded / 12.0 + 1e-9);
            double rem = rounded - feet * 12.0;
            if (rem < 0)
                rem = 0;
            string inchText = Decimal(rem, s);
            string sep = s.SuppressSpaces ? "-" : " - ";
            if (feet == 0 && s.SuppressZeroFeet)
                return inchText + "\"";
            if (rem < 0.5 / factor && s.SuppressZeroInches)
                return Group(feet, s.UseDigitGrouping) + "'";
            return Group(feet, s.UseDigitGrouping) + "'" + sep + inchText + "\"";
        }

        /// <summary>Whole inches and a reduced fraction: 126 1/2.</summary>
        public static string FractionalInches(double inches, int denominator, bool grouping = false)
        {
            int den = NormalizeDenominator(denominator);
            long units = (long)Math.Round(Math.Abs(inches) * den, MidpointRounding.AwayFromZero);
            long whole = units / den;
            long num = units % den;
            long d = den;
            Reduce(ref num, ref d);
            string sign = inches < 0 && units != 0 ? "-" : "";
            if (num == 0)
                return sign + Group(whole, grouping);
            if (whole == 0)
                return sign + num.ToString(Inv) + "/" + d.ToString(Inv);
            return sign + Group(whole, grouping) + " " + num.ToString(Inv) + "/" + d.ToString(Inv);
        }

        static int NormalizeDenominator(int den)
        {
            if (den < 1)
                return 1;
            if (den > 256)
                return 256;
            return den;
        }

        static void Reduce(ref long num, ref long den)
        {
            if (num == 0)
            {
                den = 1;
                return;
            }
            long a = Math.Abs(num), b = Math.Abs(den);
            while (b != 0)
            {
                long t = a % b;
                a = b;
                b = t;
            }
            num /= a;
            den /= a;
        }

        static string Group(long value, bool grouping) =>
            grouping ? value.ToString("#,0", Inv) : value.ToString(Inv);

        /// <summary>Decimal number with the style's precision, grouping and zero suppression.</summary>
        public static string Decimal(double value, LengthStyle s) =>
            Decimal(value, s.Precision, s.UseDigitGrouping, s.SuppressTrailingZeros, s.SuppressLeadingZeros);

        public static string Decimal(double value, int precision, bool grouping, bool suppressTrailing, bool suppressLeading)
        {
            precision = Math.Max(0, Math.Min(10, precision));
            double factor = Math.Pow(10, precision);
            double rounded = Math.Round(value * factor, MidpointRounding.AwayFromZero) / factor;
            string fmt = (grouping ? "#,0" : "0") + (precision > 0 ? "." + new string('0', precision) : "");
            string text = rounded.ToString(fmt, Inv);
            if (suppressTrailing && text.Contains("."))
                text = text.TrimEnd('0').TrimEnd('.');
            if (suppressLeading)
            {
                if (text.StartsWith("0.", StringComparison.Ordinal))
                    text = text.Substring(1);
                else if (text.StartsWith("-0.", StringComparison.Ordinal))
                    text = "-" + text.Substring(2);
            }
            if (text == "-0" || (text.StartsWith("-", StringComparison.Ordinal) && IsZeroText(text)))
                text = text.Substring(1);
            return text;
        }

        /// <summary>Format a slope given as rise / run (tan of the slope angle). Infinity = vertical.</summary>
        public static string Slope(double riseOverRun, SlopeStyle style)
        {
            if (style == null)
                style = new SlopeStyle();
            if (double.IsNaN(riseOverRun))
                return "?";
            double r = Math.Abs(riseOverRun);
            bool vertical = double.IsInfinity(r) || r > 1e9;
            bool flat = r < 1e-12;
            string sym(string s) => style.ShowUnitSymbol ? s : "";
            string dec(double v) => Decimal(v, style.Precision, false, style.SuppressTrailingZeros, false);
            switch (style.Format)
            {
                case SlopeFormat.Percent:
                    return vertical ? "VERT" : dec(r * 100.0) + sym("%");
                case SlopeFormat.PerMille:
                    return vertical ? "VERT" : dec(r * 1000.0) + sym("‰");
                case SlopeFormat.DecimalDegrees:
                    return dec(vertical ? 90.0 : Math.Atan(r) * 180.0 / Math.PI) + sym("°");
                case SlopeFormat.DegreesMinutes:
                    return DegreesMinutes(vertical ? 90.0 : Math.Atan(r) * 180.0 / Math.PI);
                case SlopeFormat.RiseOver12Inches:
                    return vertical ? "VERT" : FractionalInches(r * 12.0, style.FractionDenominator) + "\" / 12\"";
                case SlopeFormat.RiseOver1Foot:
                    return vertical ? "VERT" : FractionalInches(r * 12.0, style.FractionDenominator) + "\" / 1'-0\"";
                case SlopeFormat.RiseOver120Inches:
                    return vertical ? "VERT" : FractionalInches(r * 120.0, style.FractionDenominator) + "\" / 120\"";
                case SlopeFormat.RiseOver10Feet:
                    return vertical ? "VERT" : FractionalInches(r * 120.0, style.FractionDenominator) + "\" / 10'-0\"";
                case SlopeFormat.RiseOver1000Millimeters:
                    return vertical ? "VERT" : dec(r * 1000.0) + " / 1000";
                case SlopeFormat.RatioOneToX:
                    return vertical ? "VERT" : flat ? "FLAT" : "1:" + dec(1.0 / r);
                case SlopeFormat.RatioXTo12:
                    return vertical ? "VERT" : dec(r * 12.0) + ":12";
                case SlopeFormat.Ratio12ToX:
                    return vertical ? "VERT" : flat ? "FLAT" : "12:" + dec(12.0 / r);
                case SlopeFormat.RatioXToOne:
                    return vertical ? "VERT" : dec(r) + ":1";
                case SlopeFormat.RatioXTo10:
                    return vertical ? "VERT" : dec(r * 10.0) + ":10";
                case SlopeFormat.Ratio10ToX:
                    return vertical ? "VERT" : flat ? "FLAT" : "10:" + dec(10.0 / r);
                case SlopeFormat.RiseOverRun:
                default:
                    return vertical ? "VERT" : dec(r);
            }
        }

        /// <summary>40°30' style text.</summary>
        public static string DegreesMinutes(double degrees)
        {
            double a = Math.Abs(degrees);
            long totalMinutes = (long)Math.Round(a * 60.0, MidpointRounding.AwayFromZero);
            long d = totalMinutes / 60;
            long m = totalMinutes % 60;
            string sign = degrees < 0 && totalMinutes != 0 ? "-" : "";
            return sign + d.ToString(Inv) + "°" + m.ToString("00", Inv) + "'";
        }

        /// <summary>Azimuth (degrees clockwise from north, 0-360) as a surveyor's bearing: N 45°00' E, S 12°30' W.</summary>
        public static string Bearing(double azimuthDegrees)
        {
            double a = ((azimuthDegrees % 360) + 360) % 360;
            string ns, ew;
            double angle;
            if (a <= 90) { ns = "N"; ew = "E"; angle = a; }
            else if (a <= 180) { ns = "S"; ew = "E"; angle = 180 - a; }
            else if (a <= 270) { ns = "S"; ew = "W"; angle = a - 180; }
            else { ns = "N"; ew = "W"; angle = 360 - a; }
            long totalMinutes = (long)Math.Round(angle * 60.0, MidpointRounding.AwayFromZero);
            long d = totalMinutes / 60, m = totalMinutes % 60;
            if (d == 0 && m == 0)
                return ns == "N" ? "Due N" : "Due S";
            if (d == 90 && m == 0)
                return ew == "E" ? "Due E" : "Due W";
            return ns + " " + d.ToString(Inv) + "\u00B0" + m.ToString("00", Inv) + "' " + ew;
        }

        /// <summary>Eight-point compass name of an azimuth: N, NE, E ...</summary>
        public static string Compass(double azimuthDegrees)
        {
            string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            double a = ((azimuthDegrees % 360) + 360) % 360;
            return names[(int)Math.Floor((a + 22.5) / 45.0) % 8];
        }

        /// <summary>Angle text like "Angle to True North": 30.00° East / 12.50° West.</summary>
        public static string AngleEastWest(double degreesClockwise, int precision = 2)
        {
            if (Math.Abs(degreesClockwise) < 0.5 * Math.Pow(10, -precision))
                return Decimal(0, precision, false, false, false) + "°";
            return Decimal(Math.Abs(degreesClockwise), precision, false, false, false) + "° " + (degreesClockwise > 0 ? "East" : "West");
        }

        /// <summary>
        /// Parse a length typed by the user into metres. Accepts feet-inches (10'-6 1/2", 10' 6", 10.5'),
        /// inches (126", 126 1/2"), metric with a unit (3.2 m, 3200 mm, 320 cm) and plain numbers
        /// (interpreted in <paramref name="defaultMetersPerUnit"/>).
        /// </summary>
        public static bool TryParseLength(string text, double defaultMetersPerUnit, out double meters)
        {
            meters = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            string t = text.Trim().Replace("′", "'").Replace("″", "\"").Replace("''", "\"");
            double sign = 1;
            if (t.StartsWith("+", StringComparison.Ordinal))
                t = t.Substring(1).Trim();
            else if (t.StartsWith("-", StringComparison.Ordinal))
            {
                sign = -1;
                t = t.Substring(1).Trim();
            }
            string lower = t.ToLowerInvariant();
            if (TrySuffix(lower, "mm", 0.001, out meters) || TrySuffix(lower, "cm", 0.01, out meters) ||
                TrySuffix(lower, "km", 1000.0, out meters) || TrySuffix(lower, "m", 1.0, out meters))
            {
                meters *= sign;
                return true;
            }
            int ft = t.IndexOf('\'');
            if (ft >= 0)
            {
                if (!TryNumber(t.Substring(0, ft), out double feet))
                    return false;
                string rest = t.Substring(ft + 1).Trim().TrimStart('-').Trim().TrimEnd('"').Trim();
                double inches = 0;
                if (rest.Length > 0 && !TryInches(rest, out inches))
                    return false;
                meters = sign * (feet * MetersPerFoot + inches * MetersPerInch);
                return true;
            }
            if (t.EndsWith("\"", StringComparison.Ordinal))
            {
                if (!TryInches(t.TrimEnd('"').Trim(), out double inches))
                    return false;
                meters = sign * inches * MetersPerInch;
                return true;
            }
            if (TryInches(t, out double plain))
            {
                meters = sign * plain * defaultMetersPerUnit;
                return true;
            }
            return false;
        }

        static bool TrySuffix(string t, string suffix, double factor, out double meters)
        {
            meters = 0;
            if (!t.EndsWith(suffix, StringComparison.Ordinal))
                return false;
            string num = t.Substring(0, t.Length - suffix.Length).Trim();
            if (num.Length == 0 || !char.IsDigit(num[num.Length - 1]) && num[num.Length - 1] != '.')
                return false;
            if (!TryNumber(num, out double v))
                return false;
            meters = v * factor;
            return true;
        }

        static bool TryNumber(string s, out double v) =>
            double.TryParse(s.Trim().Replace(",", ""), NumberStyles.Float, Inv, out v);

        /// <summary>"6", "6.5", "6 1/2", "1/2".</summary>
        static bool TryInches(string s, out double inches)
        {
            inches = 0;
            s = s.Trim();
            if (s.Length == 0)
                return true;
            string[] parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            double total = 0;
            foreach (string p in parts)
            {
                int slash = p.IndexOf('/');
                if (slash > 0)
                {
                    if (!TryNumber(p.Substring(0, slash), out double n) || !TryNumber(p.Substring(slash + 1), out double d) || d == 0)
                        return false;
                    total += n / d;
                }
                else
                {
                    if (!TryNumber(p, out double w))
                        return false;
                    total += w;
                }
            }
            inches = total;
            return true;
        }

        /// <summary>Human readable name of a length format (UI).</summary>
        public static string Describe(LengthFormat f)
        {
            switch (f)
            {
                case LengthFormat.Project: return "Rhino document units";
                case LengthFormat.FeetFractionalInches: return "Feet and fractional inches";
                case LengthFormat.DecimalFeet: return "Decimal feet";
                case LengthFormat.FeetDecimalInches: return "Feet and decimal inches";
                case LengthFormat.FractionalInches: return "Fractional inches";
                case LengthFormat.DecimalInches: return "Decimal inches";
                case LengthFormat.Meters: return "Meters";
                case LengthFormat.Centimeters: return "Centimeters";
                case LengthFormat.Millimeters: return "Millimeters";
                default: return f.ToString();
            }
        }

        public static string Describe(SlopeFormat f)
        {
            switch (f)
            {
                case SlopeFormat.Percent: return "Percentage";
                case SlopeFormat.PerMille: return "Per mille";
                case SlopeFormat.DecimalDegrees: return "Decimal degrees";
                case SlopeFormat.DegreesMinutes: return "Degrees minutes";
                case SlopeFormat.RiseOver12Inches: return "Rise / 12\"";
                case SlopeFormat.RiseOver1Foot: return "Rise / 1'";
                case SlopeFormat.RiseOver120Inches: return "Rise / 120\"";
                case SlopeFormat.RiseOver10Feet: return "Rise / 10'";
                case SlopeFormat.RiseOver1000Millimeters: return "Rise / 1000 mm";
                case SlopeFormat.RatioOneToX: return "Ratio 1:x";
                case SlopeFormat.RatioXTo12: return "Ratio x:12";
                case SlopeFormat.Ratio12ToX: return "Ratio 12:x";
                case SlopeFormat.RatioXTo10: return "Ratio x:10";
                case SlopeFormat.RatioXToOne: return "Ratio x:1";
                case SlopeFormat.Ratio10ToX: return "Ratio 10:x";
                case SlopeFormat.RiseOverRun: return "Decimal (rise/run)";
                default: return f.ToString();
            }
        }

        /// <summary>Paper sizes are stored in millimetres; show them as fractional inches for imperial documents.</summary>
        public static string PaperSize(double mm, bool imperial) => PaperSize(mm, imperial ? PaperUnit.FractionalInches : PaperUnit.Millimeters);

        /// <summary>A paper size in the page units (fine enough for small sizes: 1/64", 0.001", 0.01 mm).</summary>
        public static string PaperSize(double mm, PaperUnit unit)
        {
            switch (unit)
            {
                case PaperUnit.Millimeters:
                    return Decimal(mm, 2, false, true, false) + " mm";
                case PaperUnit.DecimalInches:
                    return Decimal(mm / 25.4, 3, false, true, false) + "\"";
                default:
                    return FractionalInches(mm / 25.4, 64) + "\"";
            }
        }

        public static bool TryParsePaperSize(string text, PaperUnit unit, out double mm) =>
            TryParsePaperSize(text, unit != PaperUnit.Millimeters, out mm);

        /// <summary>A resolved length style in words, e.g. "Feet and fractional inches, 1/8\"" or "Millimeters, 0.0".</summary>
        public static string Describe(LengthStyle s)
        {
            if (s == null)
                return "?";
            bool fractional = s.Format == LengthFormat.FeetFractionalInches || s.Format == LengthFormat.FractionalInches;
            string rounding = fractional
                ? (s.FractionDenominator <= 1 ? "1\"" : "1/" + s.FractionDenominator.ToString(Inv) + "\"")
                : (s.Precision <= 0 ? "0" : "0." + new string('0', Math.Min(s.Precision, 7)));
            return Describe(s.Format) + ", " + rounding;
        }

        public static bool TryParsePaperSize(string text, bool imperial, out double mm)
        {
            mm = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            if (!TryParseLength(text, imperial ? MetersPerInch : 0.001, out double meters))
                return false;
            mm = meters * 1000.0;
            return mm > 0;
        }

        /// <summary>Text with a prefix/suffix and an optional indicator ("Indicator as Prefix / Suffix").</summary>
        public static string Compose(string prefix, string indicator, string value, string suffix, bool indicatorAsPrefix)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(prefix))
                sb.Append(prefix);
            if (indicatorAsPrefix && !string.IsNullOrEmpty(indicator))
                sb.Append(indicator);
            sb.Append(value);
            if (!indicatorAsPrefix && !string.IsNullOrEmpty(indicator))
                sb.Append(indicator);
            if (!string.IsNullOrEmpty(suffix))
                sb.Append(suffix);
            return sb.ToString();
        }
    }
}
