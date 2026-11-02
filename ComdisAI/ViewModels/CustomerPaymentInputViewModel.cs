using System.ComponentModel.DataAnnotations;
using ComdisAI.Models;

namespace ComdisAI.ViewModels;

public class CustomerPaymentInputViewModel
{
    public int Id { get; set; }

    [Required, Display(Name = "Customer")]
    public int? CustomerId { get; set; }

    [Required, DataType(DataType.Date), Display(Name = "Payment date")]
    public DateTime? PaymentDate { get; set; } = DateTime.Today;

    [Required, Range(typeof(decimal), "0.0001", "99999999999999.9999")]
    public decimal? Amount { get; set; }

    [Required]
    public PaymentMethod? Method { get; set; }

    [StringLength(100)]
    public string? Reference { get; set; }

    [Display(Name = "Bank")]
    public int? BankId { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}
