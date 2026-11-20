

using Jersey_ecommerce.Areas.Identity;
using Jersey_ecommerce.Services;
using Jersey_ecommerce.ViewModels.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Jersey_ecommerce.Controllers
{
    public class AccountController : Controller
    {
        // fields
        private readonly UserManager<ApplicationUser> _appUserManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ProfileImageService _profileImages;
        private readonly ILogger<AccountController> _logger;

        // constructor
        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            RoleManager<IdentityRole> roleManager,
            ProfileImageService profileImages,
            ILogger<AccountController> logger)
        {
            _appUserManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
            _profileImages = profileImages;
            _logger = logger;
        }

        // =========================================
        // REGISTER - GET
        // =========================================

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Register(
            string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                var currentUser =
                    await _appUserManager.GetUserAsync(User);

                if (currentUser != null)
                {
                    return await RedirectAfterLoginAsync(
                        currentUser,
                        returnUrl
                    );
                }
            }

            return View(new RegisterViewModel
            {
                ReturnUrl = returnUrl
            });
        }

        // =========================================
        // REGISTER - POST
        // =========================================

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(3 * 1024 * 1024)]
        public async Task<IActionResult> Register(
            RegisterViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            string email = model.Email.Trim();

            // Check existing user
            if (await _appUserManager.FindByEmailAsync(email) != null)
            {
                ModelState.AddModelError(
                    nameof(model.Email),
                    "An account with this email already exists."
                );

                return View(model);
            }

            // Check Customer role
            if (!await _roleManager.RoleExistsAsync("Customer"))
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Customer registration is temporarily unavailable."
                );

                return View(model);
            }

            // Upload profile image
            string? profileImageUrl = null;

            try
            {
                if (model.ProfileImage != null)
                {
                    profileImageUrl =
                        await _profileImages.SaveAsync(
                            model.ProfileImage,
                            HttpContext.RequestAborted
                        );
                }
            }
            catch (InvalidDataException ex)
            {
                ModelState.AddModelError(
                    nameof(model.ProfileImage),
                    ex.Message
                );

                return View(model);
            }
            catch (IOException ex)
            {
                _logger.LogError(
                    ex,
                    "Could not upload registration picture."
                );

                ModelState.AddModelError(
                    nameof(model.ProfileImage),
                    "Could not upload your picture."
                );

                return View(model);
            }

            // Create ApplicationUser
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,

                FullName = model.FullName.Trim(),

                PhoneNumber =
                    string.IsNullOrWhiteSpace(model.PhoneNumber)
                        ? null
                        : model.PhoneNumber.Trim(),

                ProfileImageUrl = profileImageUrl,

                CreatedAt = DateTime.UtcNow,

                IsActive = true
            };

            // Save user and hashed password
            var createResult =
                await _appUserManager.CreateAsync(
                    user,
                    model.Password
                );

            if (!createResult.Succeeded)
            {
                _profileImages.Delete(profileImageUrl);

                foreach (var error in createResult.Errors)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        error.Description
                    );
                }

                return View(model);
            }

            // Assign Customer role
            IdentityResult roleResult;

            try
            {
                roleResult = await _appUserManager.AddToRoleAsync(
                    user,
                    "Customer"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to assign Customer role to {UserId}",
                    user.Id
                );

                await CleanupFailedRegistrationAsync(
                    user,
                    profileImageUrl
                );

                ModelState.AddModelError(
                    string.Empty,
                    "Could not complete registration. Please try again."
                );

                return View(model);
            }

            if (!roleResult.Succeeded)
            {
                await CleanupFailedRegistrationAsync(
                    user,
                    profileImageUrl
                );

                foreach (var error in roleResult.Errors)
                {
                    _logger.LogError(
                        "Customer role assignment error: {Error}",
                        error.Description
                    );
                }

                ModelState.AddModelError(
                    string.Empty,
                    "Could not complete registration. Please try again."
                );

                return View(model);
            }

            // =========================================
            // REGISTRATION COMPLETED
            // DO NOT AUTO LOGIN
            // REDIRECT TO CUSTOM LOGIN PAGE
            // =========================================

            TempData["Success"] =
                "Registration successful! Please login to continue.";

            return RedirectToAction(
                nameof(Login),
                "Account",
                new { area = "" }
            );
        }

        // =========================================
        // LOGIN - GET
        // =========================================

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Login(
            string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                var currentUser =
                    await _appUserManager.GetUserAsync(User);

                if (currentUser != null)
                {
                    return await RedirectAfterLoginAsync(
                        currentUser,
                        returnUrl
                    );
                }
            }

            return View(new LoginViewModel
            {
                ReturnUrl = returnUrl
            });
        }

        // =========================================
        // LOGIN - POST
        // =========================================

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(
            LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _appUserManager.FindByEmailAsync(
                model.Email.Trim()
            );

            if (user == null || !user.IsActive)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Invalid email or password."
                );

                return View(model);
            }

            var result =
                await _signInManager.PasswordSignInAsync(
                    user,
                    model.Password,
                    model.RememberMe,
                    lockoutOnFailure: true
                );

            if (result.Succeeded)
            {
                return await RedirectAfterLoginAsync(
                    user,
                    model.ReturnUrl
                );
            }

            if (result.IsLockedOut)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Your account is temporarily locked. Try again later."
                );
            }
            else if (result.RequiresTwoFactor)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "This account requires two-factor authentication."
                );
            }
            else if (result.IsNotAllowed)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Sign-in is not allowed for this account."
                );
            }
            else
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Invalid email or password."
                );
            }

            return View(model);
        }

        // =========================================
        // LOGOUT - POST
        // =========================================

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();

            return RedirectToAction(
                "Index",
                "Home",
                new { area = "" }
            );
        }

        // =========================================
        // PROFILE - GET
        // =========================================

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Profile()
        {
            var user =
                await _appUserManager.GetUserAsync(User);

            if (user == null)
                return Challenge();

            var model = new ProfileViewModel
            {
                FullName = user.FullName,

                Email = user.Email ?? string.Empty,

                PhoneNumber = user.PhoneNumber,

                CurrentImageUrl = user.ProfileImageUrl
            };

            return View(model);
        }

        // =========================================
        // PROFILE - POST
        // =========================================

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(3 * 1024 * 1024)]
        public async Task<IActionResult> Profile(
            ProfileViewModel model)
        {
            var user =
                await _appUserManager.GetUserAsync(User);

            if (user == null)
                return Challenge();

            model.Email = user.Email ?? string.Empty;
            model.CurrentImageUrl = user.ProfileImageUrl;

            ModelState.Remove(nameof(model.Email));

            if (!ModelState.IsValid)
                return View(model);

            string? newImage = null;

            try
            {
                if (model.ProfileImage != null)
                {
                    newImage =
                        await _profileImages.SaveAsync(
                            model.ProfileImage,
                            HttpContext.RequestAborted
                        );
                }
            }
            catch (InvalidDataException ex)
            {
                ModelState.AddModelError(
                    nameof(model.ProfileImage),
                    ex.Message
                );

                return View(model);
            }
            catch (IOException ex)
            {
                _logger.LogError(
                    ex,
                    "Profile picture update failed for {UserId}",
                    user.Id
                );

                ModelState.AddModelError(
                    nameof(model.ProfileImage),
                    "Could not upload your picture."
                );

                return View(model);
            }

            string? previousImage = user.ProfileImageUrl;

            user.FullName = model.FullName.Trim();

            user.PhoneNumber =
                string.IsNullOrWhiteSpace(model.PhoneNumber)
                    ? null
                    : model.PhoneNumber.Trim();

            if (newImage != null)
            {
                user.ProfileImageUrl = newImage;
            }

            var updateResult =
                await _appUserManager.UpdateAsync(user);

            if (!updateResult.Succeeded)
            {
                _profileImages.Delete(newImage);

                model.CurrentImageUrl = previousImage;

                foreach (var error in updateResult.Errors)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        error.Description
                    );
                }

                return View(model);
            }

            if (newImage != null)
            {
                _profileImages.Delete(previousImage);
            }

            TempData["Success"] =
                "Your profile has been updated successfully.";

            return RedirectToAction(nameof(Profile));
        }

        // =========================================
        // ACCESS DENIED
        // =========================================

        [HttpGet]
        [AllowAnonymous]
        public IActionResult AccessDenied()
        {
            return View();
        }

        // =========================================
        // LOGIN REDIRECT
        // =========================================

        private async Task<IActionResult> RedirectAfterLoginAsync(
            ApplicationUser user,
            string? returnUrl)
        {
            // Redirect to requested local page
            if (!string.IsNullOrWhiteSpace(returnUrl)
                && Url.IsLocalUrl(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }

            // Admin goes to dashboard
            if (await _appUserManager.IsInRoleAsync(
                user,
                "Admin"))
            {
                return RedirectToAction(
                    "Index",
                    "Dashboard",
                    new { area = "Admin" }
                );
            }

            // Customer goes to homepage
            return RedirectToAction(
                "Index",
                "Home",
                new { area = "" }
            );
        }

        // =========================================
        // CLEAN UP FAILED REGISTRATION
        // =========================================

        private async Task CleanupFailedRegistrationAsync(
            ApplicationUser user,
            string? imageUrl)
        {
            try
            {
                var deleteResult =
                    await _appUserManager.DeleteAsync(user);

                if (!deleteResult.Succeeded)
                {
                    _logger.LogError(
                        "Could not delete incomplete registration for {UserId}",
                        user.Id
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Could not clean up registration for {UserId}",
                    user.Id
                );
            }

            _profileImages.Delete(imageUrl);
        }
    }
}
