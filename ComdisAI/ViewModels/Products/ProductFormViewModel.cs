using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ComdisAI.ViewModels.Products;

public sealed class ProductFormViewModel
{
    public int Id { get; set; }

    [Required]
    [StringLength(255)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(4)]
    [Display(Name = "Code number")]
    public string Code { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    [Display(Name = "Unit of measure")]
    public int UomId { get; set; }

    [Range(1, int.MaxValue)]
    [Display(Name = "Product category")]
    public int ProductCategoryId { get; set; }

    public IReadOnlyList<SelectListItem> Uoms { get; set; } = [];
    public IReadOnlyList<SelectListItem> ProductCategories { get; set; } = [];
}
