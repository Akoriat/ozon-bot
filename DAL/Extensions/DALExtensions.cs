using DAL.Data;
using DAL.Implementations;
using DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DAL.Extensions;

public static class DALExtensions
{
    public static IServiceCollection UseDAL(this IServiceCollection serviceCollection, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        serviceCollection.AddDbContext<OzonBotDbContext>(options =>
            options.UseNpgsql(connectionString));

        serviceCollection.AddScoped<IChatParserDataStore, ChatParserDbDataStore>();
        serviceCollection.AddScoped<INewDataRepositoryDal, NewDataRepositoryDal>();
        serviceCollection.AddScoped<IQuestionDataStore, QuestionDataStore>();
        serviceCollection.AddScoped<IReviewDataStore, ReviewDataStore>();
        serviceCollection.AddScoped<ITopicRequestDal, TopicRequestDal>();
        serviceCollection.AddScoped<ICorrectAnswerDbDataStore, CorrectAnswerDbDataStore>();
        serviceCollection.AddScoped<IAssistantModeDal, AssistantModeDal>();
        serviceCollection.AddScoped<IActiveTopicDataStore, ActiveTopicDataStore>();
        serviceCollection.AddScoped<IParsersModeDal, ParsersModeDal>();

        return serviceCollection;
    }
}