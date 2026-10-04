using ComdisAI.Models;
using ComdisAI.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace ComdisAI.Controllers;

public class OrdersController(IUnitOfWork unitOfWork) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await unitOfWork.Repository<Order>().GetAllAsync(ct));

    // Creates the order and decrements stock atomically: one SaveChanges = one transaction.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int productId, int quantity, CancellationToken ct)
    {
        if (quantity <= 0) return BadRequest("Quantity must be positive.");

        var product = await unitOfWork.Repository<Product>().GetByIdAsync(productId, ct);
        if (product is null) return NotFound();
        if (product.Stock < quantity) return BadRequest("Insufficient stock.");

        product.Stock -= quantity;
        unitOfWork.Repository<Product>().Update(product);

        await unitOfWork.Repository<Order>().AddAsync(new Order
        {
            ProductId = product.Id,
            Quantity = quantity,
            Total = product.Price * quantity
        }, ct);

        await unitOfWork.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index));
    }
}
