using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DAL.Data
{
    public class OzonBotDbContextFactory : IDesignTimeDbContextFactory<OzonBotDbContext>
    {
        public OzonBotDbContext CreateDbContext(string[] args) 
        {
            // Построение конфигурации из файла appsettings.json
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

            var builder = new DbContextOptionsBuilder<OzonBotDbContext>();
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            builder.UseNpgsql(connectionString);

            return new OzonBotDbContext(builder.Options);
        }
    }
}
