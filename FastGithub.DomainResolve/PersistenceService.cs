using AsyncKeyedLock;
using FastGithub.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace FastGithub.DomainResolve
{
    /// <summary>
    /// 域名持久化
    /// </summary>
    sealed partial class PersistenceService
    {
        private static readonly string dataFile = "dnsendpoints.json";
        private static readonly AsyncNonKeyedLocker dataLocker = new(1); 

        private readonly FastGithubConfig fastGithubConfig;
        private readonly ILogger<PersistenceService> logger;


        private record EndPointItem(string Host, int Port);

        [JsonSerializable(typeof(EndPointItem[]))]
        [JsonSourceGenerationOptions(
            WriteIndented = true,
            PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
        private partial class EndPointItemsContext : JsonSerializerContext
        {
        }


        /// <summary>
        /// 域名持久化
        /// </summary> 
        /// <param name="fastGithubConfig"></param>
        /// <param name="logger"></param>
        public PersistenceService(
            FastGithubConfig fastGithubConfig,
            ILogger<PersistenceService> logger)
        {
            this.fastGithubConfig = fastGithubConfig;
            this.logger = logger;
        }


        /// <summary>
        /// 读取保存的节点
        /// </summary>
        /// <returns></returns>
        public IList<DnsEndPoint> ReadDnsEndPoints()
        {
            if (File.Exists(dataFile) == false)
            {
                return Array.Empty<DnsEndPoint>();
            }

            using var _ = dataLocker.Lock();
            try
            {
                var utf8Json = File.ReadAllBytes(dataFile);
                var endPointItems = JsonSerializer.Deserialize(utf8Json, EndPointItemsContext.Default.EndPointItemArray);
                if (endPointItems == null)
                {
                    return Array.Empty<DnsEndPoint>();
                }

                var dnsEndPoints = new List<DnsEndPoint>();
                foreach (var item in endPointItems)
                {
                    if (this.fastGithubConfig.IsMatch(item.Host) == true)
                    {
                        dnsEndPoints.Add(new DnsEndPoint(item.Host, item.Port));
                    }
                }
                return dnsEndPoints;
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex.Message, "读取dns记录异常");
                return Array.Empty<DnsEndPoint>();
            }
        }

        /// <summary>
        /// 保存节点到文件
        /// </summary>
        /// <param name="dnsEndPoints"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task WriteDnsEndPointsAsync(IEnumerable<DnsEndPoint> dnsEndPoints, CancellationToken cancellationToken)
        {
            using var _ = await dataLocker.LockAsync();
            try
            {
                var endPointItems = dnsEndPoints.Select(item => new EndPointItem(item.Host, item.Port)).ToArray();
                var utf8Json = JsonSerializer.SerializeToUtf8Bytes(endPointItems, EndPointItemsContext.Default.EndPointItemArray);
                await File.WriteAllBytesAsync(dataFile, utf8Json, cancellationToken);
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex.Message, "保存dns记录异常");
            }
        }
    }
}
