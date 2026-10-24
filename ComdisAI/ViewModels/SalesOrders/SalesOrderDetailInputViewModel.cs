using System.ComponentModel.DataAnnotations;

namespace ComdisAI.ViewModels.SalesOrders;

public class SalesOrderDetailInputViewModel
{
    public int? Id { get; set; }

    [Range(1, int.MaxValue)]
    public int ProductId { get; set; }

    [Range(typeof(decimal), "0.0001", "99999999999999.9999")]
    public decimal Qty { get; set; }

    [Range(typeof(decimal), "0", "99999999999999.9999")]
    public decimal Price { get; set; }

    [Range(typeof(decimal), "0", "100")]
    public decimal? DiscountPercent { get; set; }
}
