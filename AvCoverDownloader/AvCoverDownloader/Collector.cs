﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AvCoverDownloader
{
    class Collector
    {
        String basePath = "https://www.dmmsee.cloud";
        public EventHandler<String> CollectorLog;
        public EventHandler<String> ErrorLog;
        SynchronizationContext _syncContext;
        //HttpClient client;

        //线程数
        const int cycleNum = 10;

        private Queue<Av> _avs;
        
        public Collector(SynchronizationContext formContext, List<Av> avs)
        {
            _avs = new Queue<Av>(avs);
            _syncContext = formContext;
        }

        public void Start()
        {
            //client = new HttpClient();
            new Thread(Collect).Start();
        }

        public void Collect()
        {
            ThreadPool.SetMinThreads(cycleNum, cycleNum);
            ThreadPool.SetMaxThreads(cycleNum, cycleNum);
            for (int i = 1; i <= cycleNum; i++)
            {
                ThreadPool.QueueUserWorkItem(new WaitCallback(DownloadFun));
            }
            
        }//method 

        public void DownloadFun(object o)
        {
            while(_avs.Count > 0)
            {
                Av a = _avs.Dequeue();

                string coverpath = a.Path + "\\cover.jpg";
                /*
                if (File.Exists(coverpath))
                {
                    _syncContext.Post(OutLog, "曾经下载过:" + a.Id + " 跳过\r\n");
                    continue;
                }
                */
                    
                string url = basePath + "/" + a.Id;
                string html = "";
                try
                {
                    //html = client.GetStringAsync(url).Result;
                    html = Download.GetHtml(url);
                }
                catch (Exception e)
                {
                    _syncContext.Post(OutError, a.Id + " :查询网站异常");
                    continue;
                }
                
                if (html.IndexOf("404 Page Not Found") != -1)
                {
                    // /search/111&parent=ce
                    _syncContext.Post(OutError, a.Id + " :找不到该番号");
                }
                else
                {
                    a.Html = html;
                    try
                    {
                        DownloadCover(a);
                    }
                    catch (Exception e)
                    {
                        _syncContext.Post(OutError, a.Id + " :采集封面失败");
                        continue;
                     }
                    
                }
            }//while
            _syncContext.Post(OutLog, "线程退出");
        }//method

        public void DownloadCover(Av av)
        {
            Match match = new Regex(@"<a class=""bigImage"" href=""(?<url>.*?)"">").Match(av.Html);
            if (match.Success)
            {
                string url = match.Groups["url"].Value;
                Download.HttpDownloadFile(url,av.Path+"\\", "art.jpg");
                //int fg = url.LastIndexOf("/") + 1;
                //string name = url.Substring(fg, url.Length - fg - 6).Trim();
                url = url.Replace("_b", "");
                string qb = "<li class=\"active\"><a href=\"" + basePath + "/\">";
                if (av.Html.IndexOf(qb) != -1)
                {
                    url = url.Replace("cover", "thumb");
                }
                else
                {
                    url = url.Replace("cover", "thumbs");
                }
                
                Download.HttpDownloadFile(url, av.Path + "\\", "folder.jpg");

                //修改文件夹名称
                try
                {
                    StringBuilder sb = new StringBuilder();
                    MatchCollection startNames = new Regex(@"<div class=""star-name""><a href="".*?"" title="".*?"">(?<name>.*?)</a></div>").Matches(av.Html);
                    int i = 0;
                    int x = 0;
                    int y = 0;
                    foreach (Match m in startNames)
                    {
                        sb.Append(m.Groups["name"].Value);
                        i++;
                        if (i < startNames.Count)
                            sb.Append(",");
                        if (++x > 20) {
                            y++; x = 0;
                            File.Create(av.Path + "\\" + av.Id + " " + sb.ToString() + ".star"+y+".txt").Close();
                            sb.Clear();
                        }
                    }
                    if(sb.Length > 0)
                    {
                        File.Create(av.Path + "\\" + av.Id + " " + sb.ToString()+".star"+(y==0?"":y+1+"")+".txt").Close();
                    }
                }
                catch (Exception e)
                {
                    _syncContext.Post(OutError, av.Id + " :获取女优失败");
                }

                try
                {
                    //match = new Regex(@"<h3>(?<title>.*?)</h3>[\s\S]*?<p><span class=""header"">發行日期:</span>(?<date>.*?)</p>").Match(av.Html);
                    match = new Regex(@"<h3>(?<title>[\s\S]*?)</h3>[\s\S]*?<p><span class=""header"">發行日期:</span>(?<date>.*?)</p>").Match(av.Html);
                    string newPath = null;
                    if (match.Success)
                    {
                        string newfolderName = match.Groups["date"].Value.Trim() + " " + match.Groups["title"].Value.Trim();
                        newfolderName = newfolderName.Replace("\\", "‖");
                        newfolderName = newfolderName.Replace("/", "‖");
                        newfolderName = newfolderName.Replace(":", "：");
                        newfolderName = newfolderName.Replace("*", "※");
                        newfolderName = newfolderName.Replace("?", "？");
                        newfolderName = newfolderName.Replace("<", "〈");
                        newfolderName = newfolderName.Replace(">", "〉");
                        newfolderName = newfolderName.Replace("\n", " ");
                        newfolderName = newfolderName.Replace("\r", " ");
                        //newfolderName = Regex.Replace(newfolderName, @"[/n/r]", " ");

                        StringBuilder rBuilder = new StringBuilder(newfolderName);
                        foreach (char rInvalidChar in Path.GetInvalidPathChars())
                            rBuilder.Replace(rInvalidChar.ToString(), string.Empty);

                        int fg = av.Path.LastIndexOf("\\");
                        string basePath = av.Path.Substring(0, fg).Trim();
                        System.IO.DirectoryInfo folder = new System.IO.DirectoryInfo(av.Path);
                        folder.MoveTo(basePath+ "\\" + newfolderName);
                        newPath = basePath + "\\" + newfolderName;
                    } 
                    else
                    {
                        ExeLog.WriteLog("获取AV名称失败:\r\n" + av.Html);
                        _syncContext.Post(OutError, av.Id + " :获取AV名称失败");
                        return;
                    }

                    //修改视频名称 改为符合 emby的规则
                    string[] extNames = new string[] { ".avi", ".mov", ".mpg", ".RA", ".RM", ".RMVB", ".WMV", ".mkv", ".mp4", ".asf", ".m4v", ".VOB"};
                    DirectoryInfo fdir = new DirectoryInfo(newPath);
                    FileInfo[] file = fdir.GetFiles();
                    if (file.Length != 0)
                    { //当前目录文件或文件夹不为空                   
                        for (int i = 0; i < extNames.Length; i++)
                        {
                            int x = 0;
                            foreach (FileInfo f in file)
                                if (extNames[i].ToLower().Equals(f.Extension.ToLower()))
                                    x++;
                            int y = 1;
                            foreach (FileInfo f in file) //显示当前目录所有文件   
                                if (extNames[i].ToLower().Equals(f.Extension.ToLower()))
                                {
                                    string newName = newPath + "\\" + av.Id + (x > 1 ? "-cd" + y : "") + f.Extension;
                                    try
                                    {
                                        //记录文件名称
                                        ChangeNameLogTxt(f.Name, newName);
                                        File.Move(f.FullName, newName);
                                        //File.Create(folder + "\\"+ (x > 1 ? "-cd" + y : "") + f.Name + ".old.name").Close();
                                    }
                                    catch (Exception ex)
                                    {
                                        _syncContext.Post(OutError, av.Id + "-修改视频文件名称错误 \r\n");
                                    }
                                    y++;
                                }//for files
                        }//for ext
                    }//file
                }
                catch (Exception e)
                {
                    _syncContext.Post(OutError, av.Id + " :修改AV名称失败" + e.Message);
                }


                _syncContext.Post(OutLog, av.Id);
            }
        }

        /// <summary>
        /// 旧文件名记录操作
        /// </summary>
        public static void ChangeNameLogTxt(string content, string fileName)
        {
            string fileStr = fileName + ".oldame.txt";
            if (!File.Exists(fileStr))
            {
                FileStream fs1 = new FileStream(fileStr, FileMode.Create, FileAccess.Write);//创建写入文件
                StreamWriter sw = new StreamWriter(fs1);
                sw.WriteLine(content);
                sw.Close();
                fs1.Close();
            }
            else
            {
                StreamWriter sw1;
                sw1 = File.AppendText(fileStr);
                sw1.WriteLine(content);
                sw1.Close();
            }
        }

        private void OutLog(object state)
        {
            CollectorLog?.Invoke(this, state.ToString());
        }

        private void OutError(object state)
        {
            ErrorLog?.Invoke(this, state.ToString());
        }

    }//class
}
