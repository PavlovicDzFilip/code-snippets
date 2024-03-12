using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NonCoveringIndexQuery;
using NonCoveringIndexQuery.Infrastructure;
using WhereIn;

var configuration = Startup.BuildConfiguration();
var serviceProvider = Startup.Configure(configuration);

var deployment = serviceProvider.GetRequiredService<Deployment>();
deployment.DeployInfrastructure();

using var scope = serviceProvider.CreateScope();
var dbContext = scope.ServiceProvider.GetRequiredService<NotificationContext>();

var startDate = DateTime.Parse("2024-03-11 17:22:05");
var endDate = startDate.AddMinutes(10);

var notificationsRisky = await GetNotificationsRisky(startDate, endDate);
var notificationsSafe = await GetNotificationsSafe(startDate, endDate);

Task<Notification[]> GetNotificationsRisky(DateTime startDate, DateTime endDate)
{
    return dbContext.Notifications
        .Where(x => x.SendDate > startDate && x.SendDate <= endDate)
        .ToArrayAsync();
}

async Task<Notification[]> GetNotificationsSafe(DateTime startDate, DateTime endDate)
{
    var notificationIds = await dbContext.Notifications
        .Where(x => x.SendDate > startDate && x.SendDate <= endDate)
        .Select(x => x.Id)
        .ToArrayAsync();

    return await dbContext.Notifications
        .WhereIn(x => x.Id, notificationIds)
        .ToArrayAsync();
}