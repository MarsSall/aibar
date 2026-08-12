namespace AIBar.Packaging.Supervisor;

internal static class Program
{
    private static async Task<int> Main()
    {
        var response = await SupervisorProtocol.HandleAsync(Console.OpenStandardInput());
        await Console.Out.WriteAsync(SupervisorProtocol.Serialize(response));
        return response.Status == SupervisorStatus.Success ? 0 : 1;
    }
}
