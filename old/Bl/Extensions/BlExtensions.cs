using Bl.Implementations;
using Bl.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Ozon.Bot.Services;

namespace Bl.Extensions;

public static class BLExtensions
{
    public static IServiceCollection UseBL(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddScoped<IChatParserBl, ChatParserBl>();
        serviceCollection.AddScoped<IChatParserService, ChatParserService>();
        serviceCollection.AddScoped<INewDataRepositoryBl, NewDataRepositoryBl>();
        serviceCollection.AddScoped<IOzonReviewParserService, OzonReviewParserService>();
        serviceCollection.AddScoped<IParserForOzonSellerService, ParserForOzonSellerService>();
        serviceCollection.AddScoped<IQuestionDataStoreBl, QuestionDataStoreBl>();
        serviceCollection.AddScoped<IReviewDataStoreBl, ReviewDataStoreBl>();
        serviceCollection.AddScoped<IChatGPTClient, ChatGPTClient>();
        serviceCollection.AddScoped<IBotService, BotService>();
        serviceCollection.AddScoped<IForumTopicService, ForumTopicService>();
        serviceCollection.AddScoped<IAnswerClientService, AnswerClientService>();
        serviceCollection.AddScoped<ITopicRequestBl, TopicRequestBl>();
        serviceCollection.AddScoped<ICorrectAnswerBl, CorrectAnswerBl>();
        serviceCollection.AddScoped<IAssistantModeBl, AssistantModeBl>();
        serviceCollection.AddScoped<IActiveTopicBl, ActiveTopicBl>();
        serviceCollection.AddScoped<IRefreshTopicsService, RefreshTopicsService>();
        serviceCollection.AddScoped<IParsersModeBl, ParsersModeBl>();

        return serviceCollection;
    }

}