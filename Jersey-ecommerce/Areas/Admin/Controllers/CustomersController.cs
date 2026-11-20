using Jersey_ecommerce.Areas.Admin.ViewModels;
using Jersey_ecommerce.Areas.Identity;
using Jersey_ecommerce.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    [Route("Admin/Customers")]
    public class CustomersController : Controller
    {
        private const int PageSize = 12;
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<CustomersController> _logger;

        public CustomersController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            ILogger<CustomersController> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        // GET: /Admin/Customers
        [HttpGet("")]
        public async Task<IActionResult> Index(string? search, string status = "all", int page = 1)
        {
            search = search?.Trim();
            status = status is "active" or "inactive" ? status : "all";

            // Keep staff/admin accounts out of the Customers module.
            var adminIds = (await _userManager.GetUsersInRoleAsync("Admin"))
                .Select(u => u.Id)
                .ToArray();

            var allCustomers = _context.Users.AsNoTracking()
                .Where(u => !adminIds.Contains(u.Id));

            var model = new CustomersIndexViewModel
            {
                Search = search,
                Status = status,
                PageSize = PageSize,
                TotalCustomers = await allCustomers.CountAsync(),
                ActiveCustomers = await allCustomers.CountAsync(u => u.IsActive),
                InactiveCustomers = await allCustomers.CountAsync(u => !u.IsActive)
            };

            var query = allCustomers;
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(u =>
                    u.FullName.Contains(search) ||
                    (u.Email != null && u.Email.Contains(search)) ||
                    (u.PhoneNumber != null && u.PhoneNumber.Contains(search)));
            }

            if (status == "active")
                query = query.Where(u => u.IsActive);
            else if (status == "inactive")
                query = query.Where(u => !u.IsActive);

            model.TotalFiltered = await query.CountAsync();
            page = Math.Clamp(page, 1, model.TotalPages);
            model.Page = page;

            model.Customers = await query
                .OrderByDescending(u => u.CreatedAt)
                .ThenBy(u => u.Id)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(u => new CustomerRowViewModel
                {
                    Id = u.Id,
                    FullName = u.FullName,
                    Email = u.Email ?? "",
                    PhoneNumber = u.PhoneNumber,
                    IsActive = u.IsActive,
                    CreatedAt = u.CreatedAt
                })
                .ToListAsync();

            var ids = model.Customers.Select(c => c.Id).ToList();
            if (ids.Count > 0)
            {
                var stats = await _context.Orders.AsNoTracking()
                    .Where(o => o.UserId != null && ids.Contains(o.UserId))
                    .GroupBy(o => o.UserId)
                    .Select(g => new
                    {
                        UserId = g.Key,
                        Count = g.Count(),
                        Spent = g.Sum(o => o.TotalAmount)
                    })
                    .ToListAsync();

                var statsByUser = stats
                    .Where(s => s.UserId != null)
                    .ToDictionary(s => s.UserId!);

                foreach (var customer in model.Customers)
                {
                    if (statsByUser.TryGetValue(customer.Id, out var stat))
                    {
                        customer.OrderCount = stat.Count;
                        customer.TotalSpent = stat.Spent;
                    }
                }
            }

            return View(model);
        }

        // GET: /Admin/Customers/Details/{id}
        [HttpGet("Details/{id}")]
        public async Task<IActionResult> Details(string id)
        {
            var user = await FindCustomerAsync(id);
            if (user == null) return NotFound();

            var model = new CustomerDetailsViewModel
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email ?? "",
                PhoneNumber = user.PhoneNumber,
                EmailConfirmed = user.EmailConfirmed,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                TotalOrders = await _context.Orders.CountAsync(o => o.UserId == id),
                TotalSpent = await _context.Orders
                    .Where(o => o.UserId == id)
                    .SumAsync(o => (decimal?)o.TotalAmount) ?? 0m,
                Addresses = await _context.Addresses.AsNoTracking()
                    .Where(a => a.UserId == id)
                    .OrderByDescending(a => a.IsDefault)
                    .ThenByDescending(a => a.CreatedAt)
                    .Select(a => new CustomerAddressViewModel
                    {
                        Id = a.Id,
                        FullName = a.FullName,
                        PhoneNumber = a.PhoneNumber,
                        AddressLine1 = a.AddressLine1,
                        AddressLine2 = a.AddressLine2,
                        City = a.City,
                        District = a.District,
                        PostalCode = a.PostalCode,
                        Country = a.Country,
                        IsDefault = a.IsDefault
                    }).ToListAsync(),
                RecentOrders = await _context.Orders.AsNoTracking()
                    .Where(o => o.UserId == id)
                    .OrderByDescending(o => o.CreatedAt)
                    .Take(20)
                    .Select(o => new CustomerOrderViewModel
                    {
                        Id = o.Id,
                        OrderNumber = o.OrderNumber,
                        Status = o.Status.ToString(),
                        CreatedAt = o.CreatedAt,
                        TotalAmount = o.TotalAmount
                    }).ToListAsync()
            };

            return View(model);
        }

        // GET: /Admin/Customers/Create
        [HttpGet("Create")]
        public IActionResult Create() => View("Create", new CustomerFormViewModel { IsActive = true });

        // POST: /Admin/Customers/Create
        [HttpPost("Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CustomerFormViewModel model)
        {
            CleanInput(model);
            if (string.IsNullOrWhiteSpace(model.Password))
                ModelState.AddModelError(nameof(model.Password), "A password is required for a new customer.");
            if (string.IsNullOrWhiteSpace(model.FullName))
                ModelState.AddModelError(nameof(model.FullName), "Enter a customer name.");
            if (!string.IsNullOrEmpty(model.Password) && model.Password != model.ConfirmPassword)
                ModelState.AddModelError(nameof(model.ConfirmPassword), "The passwords do not match.");

            if (!ModelState.IsValid) return View("Create", model);

            if (await _userManager.FindByEmailAsync(model.Email) != null)
            {
                ModelState.AddModelError(nameof(model.Email), "That email is already registered.");
                return View("Create", model);
            }

            var user = new ApplicationUser
            {
                FullName = model.FullName,
                UserName = model.Email,
                Email = model.Email,
                PhoneNumber = model.PhoneNumber,
                EmailConfirmed = true, // Created directly by an authorized administrator.
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            // Create via Identity, never insert directly into AspNetUsers.
            IdentityResult result;
            try
            {
                result = await _userManager.CreateAsync(user, model.Password!);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Customer creation failed for {Email}", model.Email);
                ModelState.AddModelError(string.Empty,
                    "Unable to create the customer due to a server/database error. Check the application logs.");
                return View("Create", model);
            }
            if (!result.Succeeded)
            {
                AddIdentityErrors(result);
                return View("Create", model);
            }

            // This project currently seeds only the Admin role; ordinary accounts
            // are customers without requiring a separate Customer role.
            if (!model.IsActive)
            {
                var stateResult = await SetAccountActiveAsync(user, false);
                if (!stateResult.Succeeded)
                {
                    AddIdentityErrors(stateResult);
                    TempData["Error"] = "Account created, but it could not be deactivated. Open the customer profile to retry.";
                    return RedirectToAction(nameof(Details), new { id = user.Id });
                }
            }

            TempData["Success"] = "Customer account created successfully.";
            return RedirectToAction(nameof(Details), new { id = user.Id });
        }

        // GET: /Admin/Customers/Edit/{id}
        [HttpGet("Edit/{id}")]
        public async Task<IActionResult> Edit(string id)
        {
            var user = await FindCustomerAsync(id);
            if (user == null) return NotFound();

            return View("Create", new CustomerFormViewModel
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email ?? "",
                PhoneNumber = user.PhoneNumber,
                IsActive = user.IsActive
            });
        }

        // POST: /Admin/Customers/Edit/{id}
        [HttpPost("Edit/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, CustomerFormViewModel model)
        {
            if (id != model.Id) return BadRequest();
            var user = await FindCustomerAsync(id);
            if (user == null) return NotFound();

            CleanInput(model);
            if (!string.IsNullOrWhiteSpace(model.Password) && model.Password != model.ConfirmPassword)
                ModelState.AddModelError(nameof(model.ConfirmPassword), "The passwords do not match.");
            if (!ModelState.IsValid) return View("Create", model);

            var matchingEmail = await _userManager.FindByEmailAsync(model.Email);
            if (matchingEmail != null && matchingEmail.Id != user.Id)
            {
                ModelState.AddModelError(nameof(model.Email), "That email is already registered.");
                return View("Create", model);
            }

            // Use UserManager so normalized Identity fields & security stamps are updated.
            user.FullName = model.FullName;
            user.Email = model.Email;
            user.UserName = model.Email;
            user.PhoneNumber = model.PhoneNumber;

            var update = await _userManager.UpdateAsync(user);
            if (!update.Succeeded)
            {
                AddIdentityErrors(update);
                return View("Create", model);
            }

            if (!string.IsNullOrWhiteSpace(model.Password))
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var passwordResult = await _userManager.ResetPasswordAsync(user, token, model.Password);
                if (!passwordResult.Succeeded)
                {
                    AddIdentityErrors(passwordResult);
                    return View("Create", model);
                }
            }

            if (user.IsActive != model.IsActive)
            {
                var stateResult = await SetAccountActiveAsync(user, model.IsActive);
                if (!stateResult.Succeeded)
                {
                    AddIdentityErrors(stateResult);
                    return View("Create", model);
                }
            }
            else
            {
                // Changes to email/phone/name or password should invalidate old cookies.
                await _userManager.UpdateSecurityStampAsync(user);
            }

            TempData["Success"] = "Customer updated successfully.";
            return RedirectToAction(nameof(Details), new { id = user.Id });
        }

        // POST: /Admin/Customers/ToggleStatus/{id}
        [HttpPost("ToggleStatus/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(string id)
        {
            var user = await FindCustomerAsync(id);
            if (user == null) return NotFound();

            var nextStatus = !user.IsActive;
            var result = await SetAccountActiveAsync(user, nextStatus);
            if (!result.Succeeded)
            {
                TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            }
            else
            {
                TempData["Success"] = nextStatus ? "Customer activated." : "Customer deactivated and sign-in locked.";
            }

            return RedirectToAction(nameof(Details), new { id = user.Id });
        }

        // POST: /Admin/Customers/Delete/{id}
        [HttpPost("Delete/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string id)
        {
            var user = await FindCustomerAsync(id);
            if (user == null) return NotFound();

            // Avoid destroying order history and published customer reviews.
            var hasOrders = await _context.Orders.AnyAsync(o => o.UserId == id);
            var hasReviews = await _context.Reviews.AnyAsync(r => r.UserId == id);
            if (hasOrders || hasReviews)
            {
                TempData["Error"] = "This customer has order/review history. Deactivate the account instead of deleting it.";
                return RedirectToAction(nameof(Details), new { id });
            }

            try
            {
                var result = await _userManager.DeleteAsync(user);
                if (!result.Succeeded)
                {
                    TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
                    return RedirectToAction(nameof(Details), new { id });
                }
            }
            catch (DbUpdateException ex)
            {
                _logger.LogWarning(ex, "Customer {CustomerId} could not be deleted due to related records.", id);
                TempData["Error"] = "This customer has related data. Deactivate the account instead.";
                return RedirectToAction(nameof(Details), new { id });
            }

            TempData["Success"] = "Customer deleted successfully.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<ApplicationUser?> FindCustomerAsync(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            var user = await _userManager.FindByIdAsync(id);
            if (user == null || await _userManager.IsInRoleAsync(user, "Admin")) return null;
            return user;
        }

        private async Task<IdentityResult> SetAccountActiveAsync(ApplicationUser user, bool active)
        {
            if (!active)
            {
                var enableLockout = await _userManager.SetLockoutEnabledAsync(user, true);
                if (!enableLockout.Succeeded) return enableLockout;
                var lockResult = await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
                if (!lockResult.Succeeded) return lockResult;
            }
            else
            {
                var unlockResult = await _userManager.SetLockoutEndDateAsync(user, null);
                if (!unlockResult.Succeeded) return unlockResult;
            }

            user.IsActive = active;
            var update = await _userManager.UpdateAsync(user);
            if (!update.Succeeded) return update;
            return await _userManager.UpdateSecurityStampAsync(user);
        }

        private void AddIdentityErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
        }

        private static void CleanInput(CustomerFormViewModel model)
        {
            model.FullName = model.FullName?.Trim() ?? "";
            model.Email = model.Email?.Trim() ?? "";
            model.PhoneNumber = string.IsNullOrWhiteSpace(model.PhoneNumber)
                ? null : model.PhoneNumber.Trim();
        }
    }
}
