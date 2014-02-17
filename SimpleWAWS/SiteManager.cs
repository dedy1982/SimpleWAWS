﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Kudu.Client.Zip;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Management.WebSites;
using Microsoft.WindowsAzure.Management.WebSites.Models;

namespace SimpleWAWS
{
    public class SiteManager: IDisposable
    {
        private bool _disposed;
        private readonly SiteNameGenerator _nameGenerator = new SiteNameGenerator();
        private readonly int _freeListSize;
        private readonly TimeSpan _siteExpiryTime;

        private readonly Queue<Site> _freeSites = new Queue<Site>();
        private readonly Dictionary<string, Site> _sitesInUse = new Dictionary<string, Site>();

        private Timer _timer;
        private readonly JobHost _jobHost = new JobHost();

        private static object _lock = new object();

        public const string InUseMetadataKey = "IN_USE";

        private static SiteManager _instance;
        public static async Task<SiteManager> GetInstanceAsync()
        {
            // TODO: what's the right way of locking when using async?
            if (_instance == null)
            {
                _instance = new SiteManager();
                await _instance.LoadSiteListFromAzureAsync();
            }

            return _instance;
        }

        public SiteManager()
        {
            var cert = new X509Certificate2(Convert.FromBase64String(ConfigurationManager.AppSettings["certificate"]));

            var creds = new CertificateCloudCredentials(ConfigurationManager.AppSettings["subscription"], cert);

            Client = new WebSiteManagementClient(creds);

            WebSpaceName = ConfigurationManager.AppSettings["webspace"];
            _freeListSize = Int32.Parse(ConfigurationManager.AppSettings["freeListSize"]);
            _siteExpiryTime = TimeSpan.FromMinutes(Int32.Parse(ConfigurationManager.AppSettings["siteExpiryMinutes"]));
        }

        public WebSiteManagementClient Client { get; set; }
        public string WebSpaceName { get; set; }

        public async Task LoadSiteListFromAzureAsync()
        {
            var sites = await Client.WebSpaces.ListWebSitesAsync(WebSpaceName, new WebSiteListParameters());
            foreach (WebSite webSite in sites)
            {
                WebSiteGetConfigurationResponse config = await Client.WebSites.GetConfigurationAsync(WebSpaceName, webSite.Name);

                var site = new Site(this, webSite, config);

                // Check if the site is free or in use and place it in the right list
                if (config.Metadata.ContainsKey(InUseMetadataKey))
                {
                    Trace.TraceInformation("Loading site {0} into the InUse list", site.Name);
                    _sitesInUse[site.Id] = site;
                }
                else
                {
                    Trace.TraceInformation("Loading site {0} into the Free list", site.Name);
                    _freeSites.Enqueue(site);
                }
            }

            await MaintainSiteLists();

            // Do maintenance on the site lists every minute
            _timer = new Timer(OnTimerElapsed);
            _timer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(60 * 1000));
        }

        public async Task MaintainSiteLists()
        {
            await AddNewSitesToFreeListAsync();
            await DeleteExpiredSitesAsync();
        }

        private void OnTimerElapsed(object state)
        {
            _jobHost.DoWork(() => { MaintainSiteLists().Wait(); });
        }

        public async Task AddNewSitesToFreeListAsync()
        {
            // If the free list doesn't have enough sites, create some more
            while (_freeSites.Count < _freeListSize)
            {
                Site site = await CreateSiteAsync();
                Trace.TraceInformation("Adding new site {0} to the free list", site.Name);
                _freeSites.Enqueue(site);
            }
        }

        public async Task DeleteExpiredSitesAsync()
        {
            var siteIdsToDelete = new List<Site>();

            // Find all the expired sites
            foreach (var entry in _sitesInUse)
            {
                if (DateTime.UtcNow - entry.Value.StartTime > _siteExpiryTime)
                {
                    siteIdsToDelete.Add(entry.Value);
                }
            }

            // Delete them
            foreach (var site in siteIdsToDelete)
            {
                Trace.TraceInformation("Deleting expired site {0}", site.Name);
                _sitesInUse.Remove(site.Id);
                await Client.WebSites.DeleteAsync(WebSpaceName, site.Name, new WebSiteDeleteParameters());
            }
        }

        private async Task<Site> CreateSiteAsync()
        {
            string webSiteName = null;
            bool includeNumber = false;
            for (int i = 0; ; i++)
            {
                webSiteName = _nameGenerator.GenerateName(includeNumber);

                if ((await Client.WebSites.IsHostnameAvailableAsync(webSiteName)).IsAvailable) break;

                if (i == 1)
                {
                    // Couldn't get a simple name, so append a number to it
                    includeNumber = true;
                }
                else if (i == 4)
                {
                    // Give up after 5 attempts
                    throw new Exception("No available site name");
                }
            }

            // Create a blank new site
            WebSiteCreateResponse webSiteCreateResponse = await Client.WebSites.CreateAsync(WebSpaceName,
                new WebSiteCreateParameters
                {
                    Name = webSiteName,
                    WebSpaceName = WebSpaceName
                });

            // Turn on Monaco
            var updateParams = Util.CreateWebSiteUpdateConfigurationParameters();
            updateParams.AppSettings = new Dictionary<string, string> {
                {"WEBSITE_NODE_DEFAULT_VERSION", "0.10.21"},
                {"MONACO_EXTENSION_VERSION", "beta"},
            };
            await Client.WebSites.UpdateConfigurationAsync(WebSpaceName, webSiteName, updateParams);

            // Get all the configuration
            WebSiteGetConfigurationResponse config = await Client.WebSites.GetConfigurationAsync(WebSpaceName, webSiteName);

            return new Site(this, webSiteCreateResponse.WebSite, config);
        }

        public async Task<Site> UseSiteAsync()
        {
            Site site = _freeSites.Dequeue();

            Trace.TraceInformation("Site {0} is now in use", site.Name);

            Task markAsInUseTask = site.MarkAsInUseAsync();

            var credentials = new NetworkCredential(site.PublishingUserName, site.PublishingPassword);
            var zipManager = new RemoteZipManager(site.ScmUrl + "zip/", credentials);
            Task zipUpload = zipManager.PutZipFileAsync("site/wwwroot", @"D:\tmp\tmp\Simple.zip");

            await Task.WhenAll(markAsInUseTask, zipUpload);

            _sitesInUse[site.Id] = site;

            return site;
        }

        public Site GetSite(string id)
        {
            Site site;
            _sitesInUse.TryGetValue(id, out site);
            return site;
        }

        public async Task DeleteSite(string id)
        {
            Site site;
            _sitesInUse.TryGetValue(id, out site);

            if (site != null)
            {
                await Client.WebSites.DeleteAsync(WebSpaceName, site.Name, new WebSiteDeleteParameters());
                _sitesInUse.Remove(site.Id);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Client.Dispose();
                    Client = null;
                }

                // Indicate that the instance has been disposed.
                _disposed = true;
            }
        }

        ~SiteManager()
        {
            Dispose(false);
        }
    }

}