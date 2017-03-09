﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Security.Cryptography;
using RestSharp;

namespace AliMNS
{
    public class MQNSClient
    {
        private string url;
        private string accessKeyId;
        private string accessKeySecret;

        private string host;
        private string version = "2015-06-06";

        private RestClient restClient;

        static MQNSClient()
        {
            System.Net.ServicePointManager.MaxServicePointIdleTime = 8000;
        }

        public MQNSClient(string url, string accessKeyId, string accessKeySecret)
        {
            this.url = url;
            this.accessKeyId = accessKeyId;
            this.accessKeySecret = accessKeySecret;

            this.host = url.StartsWith("http://") ? url.Substring(7) :url;

            this.restClient = new RestClient(this.url);
        }

        public string Version
        {
            get {
                return this.version;
            }
            set {
                this.version = value;
            }
        }


        /// <summary>
        /// 同步执行api
        /// </summary>
        /// <typeparam name="TR"></typeparam>
        /// <param name="method"></param>
        /// <param name="resource"></param>
        /// <param name="headers"></param>
        /// <param name="input"></param>
        /// <returns></returns>
        internal TR execute<TR>(Method method, string resource, Dictionary<string, string> headers, MNSRequest input = null) where TR : MNSResponse, new()
        {
            //var restClient = new RestClient(this.url);
            var request = new RestRequest(resource, this.map(method));
            request.RequestFormat = DataFormat.Xml;

            this.requestInit(method,request, headers, input);

            var response = restClient.Execute<TR>(request);

            if (response.Data != null)
            {
                return response.Data;
            }
            else if ( response.StatusCode == System.Net.HttpStatusCode.OK ||response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                return null;
            }
            else if(response.StatusCode == HttpStatusCode.Created)
            {
                return null;
            }
            else
            {
                throw new MNSRequestException(string.Format("{0}:{1}", response.StatusCode, response.StatusDescription));
            }
        }

        /// <summary>
        /// 异步执行api，主用用在popmessage上，当queue的长轮询等待时间大于0时可以非阻塞执行代码
        /// </summary>
        /// <typeparam name="TR"></typeparam>
        /// <param name="method"></param>
        /// <param name="resource"></param>
        /// <param name="headers"></param>
        /// <param name="callBack"></param>
        /// <param name="input"></param>
        internal void executeAsync<TR>(Method method, string resource, Dictionary<string, string> headers, Action<TR> callBack, MNSRequest input = null) where TR : MNSResponse, new()
        {
            //var restClient = new RestClient(this.url);
            var request = new RestRequest(resource, this.map(method));
            request.RequestFormat = DataFormat.Xml;

            this.requestInit(method,request, headers, input);

            restClient.ExecuteAsync<TR>(request, IRR => {
                callBack(IRR.Data);
            });
        }

        /// <summary>
        /// 初始化参数与头部信息 
        /// </summary>
        /// <param name="request"></param>
        /// <param name="headers"></param>
        /// <param name="input"></param>
        private void requestInit(Method method,RestRequest request, Dictionary<string, string> headers, MNSRequest input)
        {
            if (!headers.ContainsKey(HeaderConst.HOST))
            {
                headers[HeaderConst.HOST] = this.host;
            }
            if (!headers.ContainsKey(HeaderConst.DATE))
            {
                headers[HeaderConst.DATE] = DateTime.Now.ToUniversalTime().ToString("r");
            }
            if (!headers.ContainsKey(HeaderConst.MQVERSION))
            {
                headers[HeaderConst.MQVERSION] = this.version;
            }

            //如果request不为空，需指定content-type,否则签名无法通过
            if (input != null)
            {
                headers[HeaderConst.CONTENTTYPE] = "text/xml";
            }

            //headers[HeaderConst.CONTENTMD5]  =m
            headers[HeaderConst.AUTHORIZATION] = authorization(method, headers, request.Resource);
            foreach (var kv in headers)
            {
                request.AddHeader(kv.Key, kv.Value);
            }

            if (input != null)
            {
                request.AddBody(input);
            }
            
        }

        public MNSQueue getQueue(string name)
        {
            var queue = new MNSQueue();
            queue.setClient(this);
            queue.setName(name);
            return queue;
        }

        private RestSharp.Method map(Method method)
        {
            switch (method)
            {
                case Method.POST:
                    return RestSharp.Method.POST;
                case Method.PUT:
                    return RestSharp.Method.PUT;
                case Method.DELETE:
                    return RestSharp.Method.DELETE;
                case Method.GET:
                    return RestSharp.Method.GET;
                default:
                    return RestSharp.Method.GET;
            }
        }

        /// <summary>
        /// 生成验证信息
        /// </summary>
        /// <param name="method"></param>
        /// <param name="headers"></param>
        /// <param name="resource"></param>
        /// <returns></returns>
        private string authorization(Method method, Dictionary<string, string> headers, string resource)
        {
            return string.Format("MNS {0}:{1}", this.accessKeyId, this.signature(method, headers, resource));
        }

        /// <summary>
        /// 签名
        /// </summary>
        /// <param name="method"></param>
        /// <param name="headers"></param>
        /// <param name="resource"></param>
        /// <returns></returns>
        private string signature(Method method, Dictionary<string, string> headers, string resource)
        {
            List<string> toSign = new List<string>();
            toSign.Add(method.ToString());
            toSign.Add(headers.ContainsKey(HeaderConst.CONTENTMD5) ? headers[HeaderConst.CONTENTMD5] : string.Empty);
            toSign.Add(headers.ContainsKey(HeaderConst.CONTENTTYPE) ? headers[HeaderConst.CONTENTTYPE] : string.Empty);
            toSign.Add(headers.ContainsKey(HeaderConst.DATE) ? headers[HeaderConst.DATE] : DateTime.Now.ToUniversalTime().ToString("r"));

            foreach (KeyValuePair<string, string> header in headers.Where(kv => kv.Key.StartsWith("x-mns")).OrderBy(kv => kv.Key))
            {
                toSign.Add(string.Format("{0}:{1}", header.Key, header.Value));
            }

            toSign.Add(resource);

            HMACSHA1 hmac = new HMACSHA1(Encoding.UTF8.GetBytes(this.accessKeySecret));
            var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", toSign)));
            return Convert.ToBase64String(hashBytes);
        }

        public enum Method
        {
            GET,
            PUT,
            POST,
            DELETE
        }

        public class HeaderConst
        {
            public const string AUTHORIZATION = "Authorization";
            public const string CONTENTTYPE = "Content-Type";
            public const string CONTENTMD5 = "Content-MD5";
            public const string MQVERSION = "x-mns-version";
            public const string HOST = "Host";
            public const string DATE = "Date";
            public const string KEEPALIVE = "Keep-Alive";
        }
    }
}
