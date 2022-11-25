using Newtonsoft.Json;
using Shadowsocks.Controller;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Shadowsocks.Model {
    public partial class Server {
        private class State {
            public TcpClient tcpClient_;
            public Stopwatch stopWatch_;
            public List<double> latencies_;
        }

        public bool DataEqual(Server compared) {
            var leftCopy = MemberwiseClone() as Server;
            var rightCopy = compared.MemberwiseClone() as Server;
            leftCopy.Latency = LATENCY_UNKNOWN;
            rightCopy.Latency = LATENCY_UNKNOWN;
            return JsonConvert.SerializeObject(leftCopy) == JsonConvert.SerializeObject(rightCopy);
        }

        public string NamePrefix(Configuration config, int PREFIX_FLAG) {
            string prefix = "[";
            if(PREFIX_FLAG == PREFIX_MENU) {
                switch(Latency) {
                    case LATENCY_UNKNOWN:
                        prefix += I18N.GetString("Unknown");
                        break;
                    case LATENCY_TESTING:
                        prefix += I18N.GetString("Testing");
                        break;
                    case LATENCY_ERROR:
                        prefix += I18N.GetString("Error");
                        break;
                    case LATENCY_PENDING:
                        prefix += I18N.GetString("Pending");
                        break;
                    default:
                        prefix += Latency.ToString() + "ms";
                        break;
                }
                if(subscription_url == "") {
                    prefix += " " + ratio + "x";
                }
            }
            else if(PREFIX_FLAG == PREFIX_LIST) {
                foreach(var subscription in config.subscriptions) {
                    if(subscription.url == subscription_url) {
                        var encoding = Encoding.GetEncoding("GB2312");
                        var cut=4;
                        if(encoding.GetByteCount(subscription.airport) < cut + 3) {
                            prefix += subscription.airport;
                            break;
                        }
                        var cut_prefix="";
                        while(true) {
                            cut_prefix = subscription.airport.Substring(0, cut);
                            var byte_count=encoding.GetByteCount(cut_prefix);
                            if(byte_count <= 4) {
                                if(byte_count < 4) {
                                    cut_prefix += ".";
                                }
                                cut_prefix += "..";
                                break;
                            }
                            else {
                                cut--;
                            }
                        }
                        prefix += cut_prefix;
                        break;
                    }
                }
            }

            prefix += "]";

            return prefix;
        }

        public void SetSubscription(Subscription subscriptionSet) {
            Subscription = subscriptionSet;
        }

        void onGettedIPAddr(IAsyncResult result) {
            State st = (State)result.AsyncState;
            try {
                IPAddress[] ips = Dns.EndGetHostAddresses(result);
                if (ips.Length < 2) {
                    st.stopWatch_.Start();
                    st.tcpClient_.BeginConnect(ips.First<IPAddress>(), server_port, new AsyncCallback(TcpingCallback), st);
                } else {
                    List<double> iplags_ = new List<double>();
                    foreach(IPAddress ip in ips) {
                        st.stopWatch_.Start();
                        var res = st.tcpClient_.BeginConnect(ip, server_port, null, null);
                        if (res.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2))) {
                            st.stopWatch_.Stop();
                            iplags_.Add(st.stopWatch_.Elapsed.TotalMilliseconds);
                        } else {
                            st.stopWatch_.Stop();
                        }
                        st.tcpClient_.Close();
                    }
                    if(iplags_.Count < 1)
                        throw new SocketException();
                }
            } catch (SocketException e) {
                Latency = LATENCY_ERROR;
                Logging.Debug($"{server} get ip error: {e}");
            }
        }

        void TcpingCallback(IAsyncResult result) {
            State st = (State)result.AsyncState;
            try {
                st.stopWatch_.Stop();
                st.tcpClient_.EndConnect(result);
                st.tcpClient_.Close();
                st.latencies_.Add(st.stopWatch_.Elapsed.TotalMilliseconds);
                st.stopWatch_.Reset();
                
                if (st.latencies_.Count > 0) {
                    Latency = (int)st.latencies_.Average();
                }
            } catch (SocketException e) {
                Latency = LATENCY_ERROR;
                Logging.Debug($"{server} TcpingCallback ip error: {e}");
            }
        }

        public void TcpingLatency() {
#if true
            State st = new State();
            st.stopWatch_ = new Stopwatch();
            st.tcpClient_ = new TcpClient();
            st.latencies_ = new List<double>();
            try {
                Dns.BeginGetHostAddresses(server, new AsyncCallback(onGettedIPAddr), st);
            } catch (Exception) {
                Latency = LATENCY_ERROR;
            }
//             for (var testTime = 0; testTime < 2; testTime++) {
//                 try {
//                 } catch (Exception) { }
//             }
#else
            Latency = LATENCY_TESTING;
            var latencies = new List<double>();
            var stopwatch = new Stopwatch();
            for(var testTime = 0; testTime < 2; testTime++) {
                try {
                    var socket = new TcpClient();
                    var ip=Dns.GetHostAddresses(server);
                    stopwatch.Start();
                    var result = socket.BeginConnect(ip[0], server_port, null, null);
                    if(result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2))) {
                        stopwatch.Stop();
                        latencies.Add(stopwatch.Elapsed.TotalMilliseconds);
                    }
                    else {
                        stopwatch.Stop();
                    }
                    socket.Close();
                }
                catch(Exception) {

                }
                stopwatch.Reset();
            }

            if(latencies.Count != 0) {
                Latency = (int) latencies.Average();
            }
            else {
                Latency = LATENCY_ERROR;
            }
#endif
            }
    }
}