namespace Luma.BrowserAgent;

public sealed class ProductCandidate
{
    public string Title { get; set; } = "";
    public decimal Price { get; set; }
    public string Currency { get; set; } = "₴";
    public string Url { get; set; } = "";
    public string Store { get; set; } = "";
    public string Gpu { get; set; } = "";
    public string Cpu { get; set; } = "";
    public int RamGb { get; set; }
    public int SsdGb { get; set; }
    public string Display { get; set; } = "";
    public string Specs { get; set; } = "";
    public double Score { get; set; }
    public string ScoreBreakdown { get; set; } = "";
    public string BuySelector { get; set; } = "";
    public bool AddedToCart { get; set; }
}
