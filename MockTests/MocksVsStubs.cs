using System.Collections.Concurrent;
using NSubstitute;

namespace MockTests;

public static class NSubstituteExtensions
{
    public static T AsSubstituteProxy<T>(this T instance) where T : class
    {
        if (!typeof(T).IsInterface)
        {
            throw new ArgumentException("T must be an interface");
        }

        var publicMethods = typeof(T).GetMethods().Where(m => m.IsPublic);
        var substitute = Substitute.For<T>();
        var argMethod = typeof(Arg).GetMethod(nameof(Arg.Any)) ?? throw new Exception();
        foreach (var method in publicMethods)
        {
            var args = method.GetParameters()
                .Select(arg => argMethod
                    .MakeGenericMethod(arg.ParameterType)
                    .Invoke(null, null))
                .ToArray();

            method.Invoke(substitute, args)
                .Returns(callInfo => method.Invoke(instance, callInfo.Args()));
        }

        return substitute;
    }
}

public class MockDeleteCustomerHandlerTests
{
    [Fact]
    public async Task WhenCustomerExists_FindsCustomerAndDeletesIt()
    {
        // Arrange
        var stripeService = Substitute.For<IStripeService>();
        var email = "test@example.com";
        var customer1 = new StripeCustomer(Guid.NewGuid().ToString(), email);
        stripeService.Search(email).Returns([customer1]);
        var handler = new DeleteCustomer.Handler(stripeService);

        var request = new DeleteCustomer.Request(email);

        // Act
        await handler.Handle(request);

        // Assert
        await stripeService.Received(1).Delete(customer1.Id);
    }


    [Fact]
    public async Task WhenCustomerDoesNotExist_Throws()
    {
        // Arrange
        var stripeService = Substitute.For<IStripeService>();
        var email = "test@example.com";
        var customer1 = new StripeCustomer(Guid.NewGuid().ToString(), email);
        var customer2 = new StripeCustomer(Guid.NewGuid().ToString(), "test2@example.com");
        stripeService.List().Returns([customer1, customer2]);
        var handler = new DeleteCustomer.Handler(stripeService);

        var request = new DeleteCustomer.Request("non-existing@example.com");

        // Act
        async Task Handle() => await handler.Handle(request);

        // Assert
        await Assert.ThrowsAsync<Exception>(Handle);
        await stripeService.DidNotReceive().Delete(Arg.Any<string>());
    }
}

public class StubDeleteCustomerHandlerTests
{
    [Fact]
    public async Task WhenCustomerExists_FindsCustomerAndDeletesIt()
    {
        // Arrange
        var stripeService = new StubStripeService();
        var email = "test@example.com";
        var customer1 = await stripeService.Create(email);
        var handler = new DeleteCustomer.Handler(stripeService);

        var request = new DeleteCustomer.Request(email);

        // Act
        await handler.Handle(request);

        // Assert
        var remainingCustomers = await stripeService.List();
        Assert.True(remainingCustomers.Count == 0);
    }

    [Fact]
    public async Task WhenCustomerDoesNotExist_Throws()
    {
        // Arrange
        var stripeService = new StubStripeService();
        var email = "test@example.com";

        var customer1 = await stripeService.Create(email);
        var customer2 = await stripeService.Create("test2@example.com");
        var handler = new DeleteCustomer.Handler(stripeService);

        var request = new DeleteCustomer.Request("non-existing@example.com");

        // Act
        async Task Handle() => await handler.Handle(request);

        // Assert
        await Assert.ThrowsAsync<Exception>(Handle);
        var remainingCustomers = await stripeService.List();
        Assert.Equivalent(remainingCustomers, new[] { customer1, customer2 }, strict: true);
    }
}

public class StubProxyDeleteCustomerHandlerTests
{
    [Fact]
    public async Task WhenCustomerExists_FindsCustomerAndDeletesIt()
    {
        // Arrange
        var stripeService = new StubStripeService().AsSubstituteProxy<IStripeService>();
        var email = "test@example.com";
        var customer1 = await stripeService.Create(email);
        var handler = new DeleteCustomer.Handler(stripeService);

        var request = new DeleteCustomer.Request(email);

        // Act
        await handler.Handle(request);

        // Assert
        var remainingCustomers = await stripeService.List();
        Assert.True(remainingCustomers.Count == 0);

        // ReSharper disable once AsyncVoidMethod
        Received.InOrder(async void () =>
        {
            await stripeService.Received(1).Search(email);
            await stripeService.Received(1).Delete(customer1.Id);
        });
    }

    [Fact]
    public async Task WhenCustomerDoesNotExist_Throws()
    {
        // Arrange
        var stripeService = new StubStripeService().AsSubstituteProxy<IStripeService>();
        var email = "test@example.com";

        var customer1 = await stripeService.Create(email);
        var customer2 = await stripeService.Create("test2@example.com");
        var handler = new DeleteCustomer.Handler(stripeService);

        var request = new DeleteCustomer.Request("non-existing@example.com");

        // Act
        async Task Handle() => await handler.Handle(request);

        // Assert
        await Assert.ThrowsAsync<Exception>(Handle);
        var remainingCustomers = await stripeService.List();
        Assert.Equivalent(remainingCustomers, new[] { customer1, customer2 }, strict: true);

        await stripeService.Received(1).Search("non-existing@example.com");
        await stripeService.DidNotReceive().Delete(Arg.Any<string>());
    }
}

public record StripeCustomer(string Id, string Email);

public interface IStripeService
{
    Task<IReadOnlyCollection<StripeCustomer>> List();
    Task<IReadOnlyCollection<StripeCustomer>> Search(string email);
    Task<StripeCustomer> Create(string email);
    Task Delete(string id);
}

public class StubStripeService : IStripeService
{
    private readonly List<StripeCustomer> _customers = new();

    public async Task<IReadOnlyCollection<StripeCustomer>> List()
        => _customers.AsReadOnly();

    public async Task<IReadOnlyCollection<StripeCustomer>> Search(string email)
        => _customers.Where(x => x.Email.Equals(email, StringComparison.OrdinalIgnoreCase)).ToList();

    public Task<StripeCustomer> Create(string email)
    {
        var customer = new StripeCustomer(Guid.NewGuid().ToString(), email);
        _customers.Add(customer);
        return Task.FromResult(customer);
    }

    public Task Delete(string id)
    {
        var customer = _customers.FirstOrDefault(x => x.Id == id);
        if (customer == null)
        {
            throw new StripeNotFoundException("Customer not found");
        }

        _customers.Remove(customer);
        return Task.CompletedTask;
    }
}

// This exception should be thrown by the stub and by the real implementation
public class StripeNotFoundException(string message) : Exception(message);

public static class DeleteCustomer
{
    public record Request(string Email);

    public class Handler(IStripeService stripeService)
    {
        public async Task Handle(Request request)
        {
            var stripeCustomers = await stripeService.Search(request.Email);
            var stripeCustomer =
                stripeCustomers.FirstOrDefault() ??
                throw new Exception("Stripe customer not found");

            await stripeService.Delete(stripeCustomer.Id);
        }

        public async Task HandleUnoptimized(Request request)
        {
            var stripeCustomers = await stripeService.List();
            var stripeCustomer =
                stripeCustomers.FirstOrDefault(c =>
                    c.Email.Equals(request.Email, StringComparison.OrdinalIgnoreCase)) ??
                throw new Exception("Stripe customer not found");

            await stripeService.Delete(stripeCustomer.Id);
        }
    }
}