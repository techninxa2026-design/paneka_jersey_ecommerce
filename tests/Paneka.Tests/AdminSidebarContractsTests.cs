using System.Reflection;
using Jersey_ecommerce.Areas.Admin.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Paneka.Tests;

/// <summary>
/// Prevent a future sidebar change from exposing a dead admin module or
/// accidentally removing admin authorization/CSRF on its mutation actions.
/// This complements controller and checkout integration tests; it does not
/// pretend to exercise a production SQL Server/browser deployment.
/// </summary>
public sealed class AdminSidebarContractsTests
{
    public static IEnumerable<object[]> Modules()
    {
        yield return new object[] { "Dashboard", typeof(DashboardController) };
        yield return new object[] { "Notifications", typeof(NotificationsController) };
        yield return new object[] { "Products", typeof(ProductsController) };
        yield return new object[] { "Categories", typeof(CategoriesController) };
        yield return new object[] { "Brands", typeof(BrandsController) };
        yield return new object[] { "Collections", typeof(CollectionsController) };
        yield return new object[] { "Variants", typeof(VariantsController) };
        yield return new object[] { "Orders", typeof(OrdersController) };
        yield return new object[] { "Payments", typeof(PaymentsController) };
        yield return new object[] { "CheckoutPaymentReview", typeof(CheckoutPaymentReviewController) };
        yield return new object[] { "Shipments", typeof(ShipmentsController) };
        yield return new object[] { "Customers", typeof(CustomersController) };
        yield return new object[] { "Reviews", typeof(ReviewsController) };
        yield return new object[] { "Coupons", typeof(CouponsController) };
        yield return new object[] { "Banners", typeof(BannersController) };
        yield return new object[] { "Settings", typeof(SettingsController) };
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Every_administration_sidebar_module_has_a_secure_real_index_action(
        string routeName, Type controllerType)
    {
        Assert.True(typeof(Controller).IsAssignableFrom(controllerType),
            $"{routeName} must be a real MVC controller.");
        Assert.Equal("Admin", controllerType.GetCustomAttribute<AreaAttribute>()?.RouteValue);

        var authorization = controllerType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorization);
        Assert.Contains("Admin", authorization!.Roles?.Split(',') ?? Array.Empty<string>());

        var index = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .SingleOrDefault(method => method.Name == "Index" &&
                method.DeclaringType == controllerType &&
                method.GetCustomAttributes<HttpGetAttribute>().Any());
        Assert.NotNull(index);
        Assert.Null(index!.GetCustomAttribute<NonActionAttribute>());
        Assert.NotEmpty(index.GetCustomAttributes<HttpGetAttribute>());
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Admin_sidebar_mutations_require_an_antiforgery_token(
        string routeName, Type controllerType)
    {
        foreach (var action in controllerType.GetMethods(
            BindingFlags.Public | BindingFlags.Instance))
        {
            if (action.DeclaringType != controllerType ||
                action.GetCustomAttributes<HttpPostAttribute>().Count() == 0)
                continue;
            Assert.True(
                action.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), inherit: true) ||
                controllerType.IsDefined(typeof(AutoValidateAntiforgeryTokenAttribute), inherit: true),
                $"{routeName}.{action.Name} must validate an anti-CSRF token.");
        }
    }
}
