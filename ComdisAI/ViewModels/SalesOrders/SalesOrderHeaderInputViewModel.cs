using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ComdisAI.ViewModels.SalesOrders;

public class SalesOrderHeaderInputViewModel
{
    [Range(1, int.MaxValue)]
    public int CustomerId { get; set; }

    [Required]
    [StringLength(359)]
    public string DeliveryAddress { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    public DateTime DeliveryDate { get; set; } = DateTime.Today;

    [Range(1, int.MaxValue)]
    public int TaxId { get; set; }

    [Range(typeof(decimal), "0", "100")]
    public decimal DiscountPercent1 { get; set; }

    [Range(typeof(decimal), "0", "100")]
    public decimal DiscountPercent2 { get; set; }

    [Range(typeof(decimal), "0", "100")]
    public decimal DiscountPercent3 { get; set; }

    public IReadOnlyList<SelectListItem> Customers { get; set; } = [];
    public IReadOnlyDictionary<string, string> CustomerAddresses { get; set; } = new Dictionary<string, string>();
    public IReadOnlyList<SelectListItem> Taxes { get; set; } = [];
}
