using Anchal.Api.CommissionerMonitoring;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Infrastructure.CommissionerMonitoring;
using Anchal.Infrastructure.CommissionerMonitoring.Auth;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCommissionerMonitoringPersistence(builder.Configuration, builder.Environment);
builder.Services.AddCommissionerMonitoringWorkflows();
builder.Services.AddMonitoringAuthentication();
builder.Services.AddAuthorization(MonitoringPolicies.AddTo);
builder.Services.AddControllers();
builder.Services.AddCommissionerMonitoringApi(builder.Configuration);
var app = builder.Build();
app.UseCommissionerMonitoringApi();
app.UseCors(CommissionerMonitoringApiRegistration.CorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program { }
