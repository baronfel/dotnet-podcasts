using Azure.Storage.Queues;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Podcast.API.Models;
using Podcast.Infrastructure.Data;
using Podcast.Infrastructure.Data.Models;
using Podcast.Infrastructure.Http.Feeds;
using Microsoft.OpenApi.Models;

namespace Podcast.API.Routes;

public static class FeedsApi
{
    public static RouteGroupBuilder MapFeedsApi(this RouteGroupBuilder group)
    {
        group.MapPost("/", CreateFeed);
        group.MapGet("/", GetAllFeeds);
        group.MapPut("/{id}", UpdateFeed).RequireAuthorization().AddOpenApiSecurityRequirement();
        group.MapDelete("/{id}", DeleteFeed).RequireAuthorization().AddOpenApiSecurityRequirement();
        return group;
    }

    private static RouteHandlerBuilder AddOpenApiSecurityRequirement(this RouteHandlerBuilder builder)
    {
        var scheme = new OpenApiSecurityScheme()
        {
            Type = SecuritySchemeType.Http,
            Name = JwtBearerDefaults.AuthenticationScheme,
            Scheme = JwtBearerDefaults.AuthenticationScheme,
            Reference = new()
            {
                Type = ReferenceType.SecurityScheme,
                Id = JwtBearerDefaults.AuthenticationScheme
            }
        };
        builder.WithOpenApi(operation => new(operation)
        {
            Security =
            {
                new()
                {
                    [scheme] = new List<string>()
                }
            }
        });
        return builder;
    }

    public static async ValueTask CreateFeed(QueueClient queueClient, UserSubmittedFeedDto feed, CancellationToken cancellationToken)
    {
        await queueClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        await queueClient.SendMessageAsync(new BinaryData(feed), cancellationToken: cancellationToken);
    }

    public static async ValueTask<Ok<List<UserSubmittedFeed>>> GetAllFeeds(PodcastDbContext podcastDbContext, CancellationToken cancellationToken)
    {
        var feeds = await podcastDbContext.UserSubmittedFeeds.OrderByDescending(f => f.Timestamp).ToListAsync(cancellationToken);
        return TypedResults.Ok(feeds);
    }

    public static async ValueTask<Results<NotFound, Accepted>> UpdateFeed(PodcastDbContext podcastDbContext, IFeedClient feedClient, Guid id, CancellationToken cancellationToken)
    {
        var feed = podcastDbContext.UserSubmittedFeeds.Find(id);
        if (feed is null)
            return TypedResults.NotFound();

        var categories = feed.Categories.Split(',');

        await feedClient.AddFeedAsync(podcastDbContext, feed.Url, categories, cancellationToken);
        podcastDbContext.Remove(feed);
        await podcastDbContext.SaveChangesAsync(cancellationToken);
        return TypedResults.Accepted($"/feeds/{id}");
    }

    public static async ValueTask<Results<NotFound, NoContent>> DeleteFeed(PodcastDbContext podcastDbContext, IFeedClient feedClient, Guid id, CancellationToken cancellationToken)
    {
        var feed = podcastDbContext.UserSubmittedFeeds.FirstOrDefault(x => x.Id == id);
        if (feed is null)
            return TypedResults.NotFound();

        podcastDbContext.Remove(feed);
        await podcastDbContext.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}
