// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0
using Amazon.DynamoDBv2;
using AWS.DistributedCacheProvider;
using AWS.DistributedCacheProvider.Internal;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ExtensionMethods
    {
        /// <summary>
        /// Injects <see cref="DynamoDBDistributedCache" /> as the implementation for <see cref="IDistributedCache"/>.
        /// </summary>
        /// <param name="services">The current ServiceCollection</param>
        /// <param name="action">An Action to configure the parameters of <see cref="DynamoDBDistributedCacheOptions"/> for the cache</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static IServiceCollection AddAWSDynamoDBDistributedCache(this IServiceCollection services, Action<DynamoDBDistributedCacheOptions> action)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            services.TryAddAWSService<IAmazonDynamoDB>();
            services.AddSingleton<IDynamoDBTableCreator, DynamoDBTableCreator>();

            services.AddOptions();
            services.Configure(action);

            services.Add(ServiceDescriptor.Singleton<IDistributedCache, DynamoDBDistributedCache>((IServiceProvider p) => 
            {
                var client = p.GetRequiredService<IAmazonDynamoDB>();
                var options = p.GetRequiredService<IOptions<DynamoDBDistributedCacheOptions>>();
                var creator = p.GetRequiredService<IDynamoDBTableCreator>();
                return new DynamoDBDistributedCache(client, creator, options.Value);
            }));

            return services;
        }
    }
}
