using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ComdisAI.ViewModels.CustomerPriceLists;

public class CustomerPriceListInputViewModel
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Select a product.")]
    [Display(Name = "Product")]
    public int? ProductId { get; set; }

    [Required]
    [Range(typeof(decimal), "0", "99999999999999.9999", ErrorMessage = "Price must be zero or greater.")]
    public decimal? Price { get; set; }

    public IReadOnlyList<SelectListItem> Products { get; set; } = [];
}
