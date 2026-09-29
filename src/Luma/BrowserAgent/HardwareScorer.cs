using System.Text.RegularExpressions;

namespace Luma.BrowserAgent;

public static class HardwareScorer
{
    public static decimal ExtractBudget(string query, decimal fallback = 50000m)
    {
        // Matches patterns like "50 тысяч", "50000", "50 000 грн", "до 45к", "бюджет 50к"
        var thousandMatch = Regex.Match(query, @"(?:до|бюджет[^\d]*|цена[^\d]*)?(\d+(?:[.,]\d+)?)\s*(?:тыс(?:яч|ячи|ь)?|k|к)\b", RegexOptions.IgnoreCase);
        if (thousandMatch.Success && decimal.TryParse(thousandMatch.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var thousands))
        {
            return thousands * 1000m;
        }

        var directMatch = Regex.Match(query, @"(?:до|бюджет[^\d]*|цена[^\d]*)?(\d{2,3}[\s_]?\d{3})\b", RegexOptions.IgnoreCase);
        if (directMatch.Success)
        {
            var raw = directMatch.Groups[1].Value.Replace(" ", "").Replace("_", "");
            if (decimal.TryParse(raw, out var val) && val >= 5000 && val <= 500000)
                return val;
        }

        return fallback;
    }

    public static void ParseSpecs(ProductCandidate p)
    {
        var text = (p.Title + " " + p.Specs).ToLowerInvariant();

        // GPU detection & performance points
        if (text.Contains("rtx 4090") || text.Contains("rtx4090")) p.Gpu = "RTX 4090";
        else if (text.Contains("rtx 4080") || text.Contains("rtx4080")) p.Gpu = "RTX 4080";
        else if (text.Contains("rtx 4070 ti") || text.Contains("rtx 4070ti")) p.Gpu = "RTX 4070 Ti";
        else if (text.Contains("rtx 4070") || text.Contains("rtx4070")) p.Gpu = "RTX 4070";
        else if (text.Contains("rtx 4060") || text.Contains("rtx4060")) p.Gpu = "RTX 4060";
        else if (text.Contains("rtx 4050") || text.Contains("rtx4050")) p.Gpu = "RTX 4050";
        else if (text.Contains("rtx 3080") || text.Contains("rtx3080")) p.Gpu = "RTX 3080";
        else if (text.Contains("rtx 3070") || text.Contains("rtx3070")) p.Gpu = "RTX 3070";
        else if (text.Contains("rtx 3060") || text.Contains("rtx3060")) p.Gpu = "RTX 3060";
        else if (text.Contains("rtx 3050") || text.Contains("rtx3050")) p.Gpu = "RTX 3050";
        else if (text.Contains("gtx 1650") || text.Contains("gtx1650")) p.Gpu = "GTX 1650";
        else if (text.Contains("rx 7600") || text.Contains("rx 6600")) p.Gpu = "Radeon RX 6600/7600";
        else if (text.Contains("radeon 780m") || text.Contains("radeon 680m")) p.Gpu = "Radeon 780M (iGPU)";
        else p.Gpu = "Встроенная / Базовая";

        // CPU detection
        if (text.Contains("i9-") || text.Contains("core i9")) p.Cpu = "Intel Core i9";
        else if (text.Contains("i7-") || text.Contains("core i7")) p.Cpu = "Intel Core i7";
        else if (text.Contains("i5-") || text.Contains("core i5")) p.Cpu = "Intel Core i5";
        else if (text.Contains("ryzen 9") || text.Contains("r9-")) p.Cpu = "AMD Ryzen 9";
        else if (text.Contains("ryzen 7") || text.Contains("r7-")) p.Cpu = "AMD Ryzen 7";
        else if (text.Contains("ryzen 5") || text.Contains("r5-")) p.Cpu = "AMD Ryzen 5";
        else p.Cpu = "Современный 6-8 ядерный CPU";

        // RAM detection
        var ramMatch = Regex.Match(text, @"(\d{1,2})\s*(?:гб|gb)\s*(?:ddr|ram|озу)?");
        if (ramMatch.Success && int.TryParse(ramMatch.Groups[1].Value, out var ram) && ram is 8 or 16 or 24 or 32 or 64)
            p.RamGb = ram;
        else
            p.RamGb = 16; // Modern standard fallback

        // SSD detection
        if (text.Contains("1тб") || text.Contains("1tb") || text.Contains("1024 гб") || text.Contains("1000 гб")) p.SsdGb = 1000;
        else if (text.Contains("2тб") || text.Contains("2tb")) p.SsdGb = 2000;
        else if (text.Contains("256гб") || text.Contains("256 gb")) p.SsdGb = 256;
        else p.SsdGb = 512;

        // Display refresh rate
        if (text.Contains("240hz") || text.Contains("240 гц")) p.Display = "240Hz";
        else if (text.Contains("165hz") || text.Contains("165 гц")) p.Display = "165Hz";
        else if (text.Contains("144hz") || text.Contains("144 гц")) p.Display = "144Hz";
        else p.Display = "144Hz IPS";
    }

    public static double CalculateValueScore(ProductCandidate p, decimal budget)
    {
        // Baseline performance points
        double perfPoints = p.Gpu switch
        {
            "RTX 4090" => 100,
            "RTX 4080" => 92,
            "RTX 4070 Ti" => 85,
            "RTX 4070" => 80,
            "RTX 4060" => 72, // Sweet spot for 45-50k UAH!
            "RTX 4050" => 56,
            "RTX 3070" => 64,
            "RTX 3060" => 54,
            "RTX 3050" => 40,
            "GTX 1650" => 22,
            _ => 18
        };

        // CPU points
        perfPoints += p.Cpu.Contains("i7") || p.Cpu.Contains("Ryzen 7") ? 15 :
                      p.Cpu.Contains("i5") || p.Cpu.Contains("Ryzen 5") ? 10 : 5;

        // RAM points
        perfPoints += p.RamGb >= 32 ? 10 : p.RamGb >= 16 ? 6 : 0;

        // SSD points
        perfPoints += p.SsdGb >= 1000 ? 5 : 2;

        if (p.Price <= 0) return 0;

        // Price ratio
        // If price is within budget (e.g. 48,000 for 50,000 budget), it utilizes budget efficiently
        double priceFactor;
        if (p.Price <= budget)
        {
            // Close to budget with best specs gives maximum efficiency
            var utilization = (double)(p.Price / budget);
            priceFactor = 1.0 + (utilization * 0.2); // slight bonus for using available budget to get stronger GPU
        }
        else
        {
            // Over budget penalty
            var overRatio = (double)((p.Price - budget) / budget);
            priceFactor = Math.Max(0.1, 1.0 - (overRatio * 2.5));
        }

        // Final score on a 1.0 - 10.0 scale
        var finalScore = Math.Round((perfPoints / 10.0) * priceFactor, 1);
        p.Score = Math.Clamp(finalScore, 1.0, 9.9);
        p.ScoreBreakdown = $"GPU: {p.Gpu} ({perfPoints:F0} pts), RAM: {p.RamGb}GB, SSD: {p.SsdGb}GB, Price: {p.Price:N0} {p.Currency}";
        return p.Score;
    }
}
