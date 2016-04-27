﻿using HtmlAgilityPack;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Xaml.Data;
using Windows.Web.Http;
using static System.Runtime.InteropServices.WindowsRuntime.AsyncInfo;
using Newtonsoft.Json;
using System.Net;

namespace ExClient
{
    [Flags]
    public enum Category : uint
    {
        Unspecified = 0,
        Doujinshi = 0x01,
        Manga = 0x02,
        ArtistCG = 0x04,
        GameCG = 0x08,
        Western = 0x10,
        NonH = 0x20,
        ImageSet = 0x40,
        Cosplay = 0x80,
        AsianPorn = 0x100,
        Misc = 0x200,
        Unknown = 0x80000000,
        All = Doujinshi | Manga | ArtistCG | GameCG | Western | NonH | ImageSet | Cosplay | AsianPorn | Misc
    }

    public class SearchResult : IncrementalLoadingCollection<Gallery>
    {
        private static readonly Uri searchUri = new Uri("http://exhentai.org/");
        public static readonly Category DefaultFliter = Category.All;
        private static readonly Dictionary<Category, string> searchFliterNames = new Dictionary<Category, string>()
        {
            [Category.Doujinshi] = "f_doujinshi",
            [Category.Manga] = "f_manga",
            [Category.ArtistCG] = "f_artistcg",
            [Category.GameCG] = "f_gamecg",
            [Category.Western] = "f_western",
            [Category.NonH] = "f_non-h",
            [Category.ImageSet] = "f_imageset",
            [Category.Cosplay] = "f_cosplay",
            [Category.AsianPorn] = "f_asianporn",
            [Category.Misc] = "f_misc"
        };

        internal static IAsyncOperation<SearchResult> SearchAsync(Client client, string keyWord, Category filter)
        {
            return Run(async token =>
            {
                if(filter == Category.Unspecified)
                    filter = DefaultFliter;
                var result = new SearchResult(client, keyWord, filter);
                var init = result.init();
                token.Register(init.Cancel);
                await init;
                return result;
            });
        }

        private SearchResult(Client client, string keyWord, Category filter)
            : base(1)
        {
            this.client = client;
            this.KeyWord = keyWord;
            this.Filter = filter;
        }

        private IAsyncAction init()
        {
            return Run(async token =>
            {
                var args = new Dictionary<string, string>()
                {
                    ["f_search"] = KeyWord
                };
                foreach(var item in searchFliterNames)
                {
                    if((Filter & item.Key) == item.Key)
                        args.Add(item.Value, "1");
                    else
                        args.Add(item.Value, "0");
                }
                args.Add("f_apply", "Apply Filter");
                var uri = new Uri(searchUri, $"?{new HttpFormUrlEncodedContent(args)}");
                searchResultBaseUri = uri.OriginalString;
                var lans = client.HttpClient.GetAsync(uri);
                IAsyncOperation<uint> lp = null;
                token.Register(() =>
                {
                    lans.Cancel();
                    lp?.Cancel();
                });
                var ans = await lans;
                using(var stream = (await ans.Content.ReadAsInputStreamAsync()).AsStreamForRead())
                {
                    var doc = new HtmlDocument();
                    doc.Load(stream);
                    var rcNode = doc.DocumentNode.Descendants("p").Where(node => node.GetAttributeValue("class", null) == "ip").Single();
                    var match = Regex.Match(rcNode.InnerText, @"Showing.+of\s+([0-9,]+)");
                    if(match.Success)
                        RecordCount = int.Parse(match.Groups[1].Value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture);
                    if(!IsEmpty)
                    {
                        var pcNodes = doc.DocumentNode.Descendants("td")
                            .Where(node => "document.location=this.firstChild.href" == node.GetAttributeValue("onclick", ""))
                            .Select(node =>
                            {
                                int i;
                                var su = int.TryParse(node.InnerText, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out i);
                                return Tuple.Create(su, i);
                            })
                            .Where(select => select.Item1)
                            .DefaultIfEmpty(Tuple.Create(true, 1))
                            .Max(select => select.Item2);
                        PageCount = pcNodes;
                        lp = loadPage(doc);
                        await lp;
                    }
                }
            });
        }

        private IAsyncOperation<uint> loadPage(HtmlDocument doc)
        {
            return Run(async token =>
            {
                var table = (from node in doc.DocumentNode.Descendants("table")
                             where node.GetAttributeValue("class", "") == "itg"
                             select node).Single();
                var records = from node in table.Descendants("tr")
                              where node.GetAttributeValue("class", null) != null
                              let detail = (from node2 in node.Descendants("a")
                                            where node2.GetAttributeValue("onmouseover", "").StartsWith("show_image_pane")
                                            select node2).SingleOrDefault()
                              where detail != null
                              let match = Regex.Match(detail.GetAttributeValue("href", ""), @".+?/g/(\d+)/([0-9a-f]+).+?")
                              where match.Success
                              select new gdataRecord
                              {
                                  gid = long.Parse(match.Groups[1].Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture),
                                  gtoken = match.Groups[2].Value
                              };
                var json = JsonConvert.SerializeObject(new
                {
                    method = "gdata",
                    @namespace = 1,
                    gidlist = records
                });
                var type = new
                {
                    gmetadata = (IEnumerable<Gallery>)null
                };
                var apiRequest = client.PostApiAsync(json);
                token.Register(apiRequest.Cancel);
                var str = await apiRequest;
                var re = JsonConvert.DeserializeAnonymousType(str, type);
                var count = 0u;
                foreach(var item in re.gmetadata)
                {
                    item.Owner = client;
                    this.Add(item);
                    count++;
                }
                return count;
            });
        }

        [JsonArray]
        private class gdataRecord : IEnumerable
        {
            public long gid;
            public string gtoken;

            public IEnumerator GetEnumerator()
            {
                yield return gid;
                yield return gtoken;
            }
        }

        public string KeyWord
        {
            get;
            private set;
        }

        public Category Filter
        {
            get;
            private set;
        }

        private Client client;

        private string searchResultBaseUri;

        protected override IAsyncOperation<uint> LoadPage(int pageIndex)
        {
            return Run(async token =>
            {
                var uri = new Uri($"{this.searchResultBaseUri}&page={pageIndex.ToString()}");
                var op = client.HttpClient.GetAsync(uri);
                IAsyncOperation<uint> op2 = null;
                token.Register(() =>
                {
                    op.Cancel();
                    op2?.Cancel();
                });
                var ans = await op;
                using(var stream = (await ans.Content.ReadAsInputStreamAsync()).AsStreamForRead())
                {
                    var doc = new HtmlDocument();
                    doc.Load(stream);
                    op2 = loadPage(doc);
                    return await op2;
                }
            });
        }
    }
}