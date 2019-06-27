using AirlineTwitterDataCollection.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Twitterizer;

namespace AirlineTwitterDataCollection
{
    public partial class FormMain : Form
    {
        delegate void SetTextCallback(string text, Color color);

        private static OAuthTokens Tokens = null;
        private OAuthTokenResponse AuthTokenResponse = null;

        public FormMain()
        {
            InitializeComponent();

            //this.Text = this.Text + System.IO.Path.g(Environment.CurrentDirectory);
            //this.Text += "::" + .GetFileName(GetDirectoryName(Application.ExecutablePath);

            this.toolStripMenuItemPath.Text = Path.GetDirectoryName(Application.ExecutablePath);

            this.Text += string.Format(":: (opened from {0})", Path.GetFileName(Path.GetDirectoryName(Application.ExecutablePath)));

            this.ContextMenuStrip = contextMenuStripMain;
            this.TextBoxInput.ContextMenuStrip = contextMenuInput;
            this.flowLayoutPanel1.ContextMenuStrip = contextMenuRadio;

            Tokens = new OAuthTokens
            {
                ConsumerKey = Settings.Default.ConsumerKey,
                ConsumerSecret = Settings.Default.ConsumerSecret
            };

            if (!string.IsNullOrEmpty(Settings.Default.UserAccessToken) && !string.IsNullOrEmpty(Settings.Default.UserAccessSecret))
            {
                Tokens.AccessToken = Settings.Default.UserAccessToken;
                Tokens.AccessTokenSecret = Settings.Default.UserAccessSecret;
            }
            else
            {
                AuthTokenResponse = OAuthUtility.GetRequestToken(Settings.Default.ConsumerKey, Settings.Default.ConsumerSecret, "oob");

                TextBoxInput.Text = "Enter pin here. Make sure the pin is the only text in this textbox. Click Authenticate.";

                Process process = new Process();
                process.StartInfo.FileName = getDefaultBrowser();
                process.StartInfo.Arguments = "http://twitter.com/oauth/authorize?oauth_token=" + AuthTokenResponse.Token;
                process.Start();

            }

            CommandManagement.Init(this, Tokens);
        }

        private string getDefaultBrowser()
        {
            string browser = string.Empty;
            Microsoft.Win32.RegistryKey key = null;
            try
            {
                key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"HTTP\shell\open\command", false);

                //trim off quotes
                browser = key.GetValue(null).ToString().ToLower().Replace("\"", "");
                if (!browser.EndsWith("exe"))
                {
                    //get rid of everything after the ".exe"
                    browser = browser.Substring(0, browser.LastIndexOf(".exe") + 4);
                }
            }
            finally
            {
                if (key != null) key.Close();
            }
            return browser;
        }

        private void ButtonAuthenticate_Click(object sender, EventArgs e)
        {
            if (AuthTokenResponse == null)
            {
                AppendLineToOutput("Already authenticated. Check ratelimiting details.");
                return;
            }

            AuthTokenResponse = OAuthUtility.GetAccessToken(Settings.Default.ConsumerKey,
                                                            Settings.Default.ConsumerSecret,
                                                            AuthTokenResponse.Token,
                                                            TextBoxInput.Text);

            var tokenResponse = AuthTokenResponse;
            Settings.Default.UserAccessToken = tokenResponse.Token;
            Settings.Default.UserAccessSecret = tokenResponse.TokenSecret;
            Settings.Default.Save();

            Tokens.AccessToken = Settings.Default.UserAccessToken;
            Tokens.AccessTokenSecret = Settings.Default.UserAccessSecret;

            CommandManagement.Init(this, Tokens);

            AppendLineToOutput("Authentication succeeded.");
        }

        private void ButtonGetFollowerList_Click(object sender, EventArgs e)
        {
            string[] screennames;

            if (TextBoxInput.Text.Trim().Length > 0)
            {
                RadioAirNZ.Checked = false;
                RadioJetStar.Checked = false;
                RadioQantas.Checked = false;
                RadioVirginAustralia.Checked = false;

                screennames = TextBoxInput.Lines;
            }
            else
            {
                screennames = new string[1];
                screennames[0] = GetSelectedAccount();

                if (screennames[0] == "")
                {
                    AppendLineToOutput(string.Format("No user selected"), Color.Maroon);
                    return;
                }
            }

            DisableFollowerControls();

            CommandResult result = CommandResult.Failure;

            BackgroundWorker bgw = new BackgroundWorker();
            bgw.DoWork += delegate
            {
                AppendLineToOutput(string.Format("Starting task [{0}]", "Get Follower List"), Color.Maroon);

                AppendLineToOutput(string.Format("{0} users to be processed.", screennames.Length), Color.Maroon);
                try
                {

                    result = CommandManagement.GetFollowerList(screennames);

                }
                catch (Exception ex)
                {
                    AppendLineToOutput(ex.Message, Color.Maroon);
                    AppendLineToOutput(ex.StackTrace, Color.Maroon);
                }
            };

            bgw.RunWorkerCompleted += delegate
            {
                AppendLineToOutput(result, Color.Maroon);
                //AppendLineToOutput(string.Format("Completed task [{0}] for [@{1}]", "Get Follower List", screenname), Color.Maroon);
                AppendLineToOutput(string.Format("Completed task [{0}]", "Get Follower List"), Color.Maroon);
                EnableFollowerControls();
            };

            bgw.RunWorkerAsync();
        }

        private string GetSelectedAccount()
        {
            if (RadioAirNZ.Checked)
            {
                return "FlyAirNZ";
            }

            if (RadioJetStar.Checked)
            {
                return "JetstarAirways";
            }

            if (RadioQantas.Checked)
            {
                return "Qantas";
            }

            if (RadioVirginAustralia.Checked)
            {
                return "VirginAustralia";
            }

            return "";
        }

        internal void AppendLineToOutput(string text, Color color)
        {
            // InvokeRequired required compares the thread ID of the
            // calling thread to the thread ID of the creating thread.
            // If these threads are different, it returns true.
            if (TextBoxOutput.InvokeRequired)
            {
                SetTextCallback d = new SetTextCallback(AppendLineToOutput);
                this.Invoke(d, new object[] { text, color });
            }
            else
            {

                TextBoxOutput.SelectionStart = TextBoxOutput.TextLength;
                TextBoxOutput.SelectionLength = 0;

                TextBoxOutput.SelectionColor = color;

                TextBoxOutput.AppendText("[" + System.DateTime.Now.ToString("dd/MM HH:mm:ss") + "] " + text + Environment.NewLine);

                TextBoxOutput.SelectionColor = TextBoxOutput.ForeColor;

                if (!this.ContainsFocus)
                {
                    TextBoxOutput.ScrollToCaret();
                }

                Console.Out.WriteLine("[" + System.DateTime.Now.ToString() + "] " + text);
            }
        }

        internal void AppendLineToOutput(string text)
        {
            AppendLineToOutput(text, Color.Black);
        }

        internal void AppendLineToOutput(CommandResult result, Color color)
        {
            AppendLineToOutput("Command result: " + Enum.GetName(typeof(CommandResult), result), color);
        }


        private void ButtonCheckRateLimit_Click(object sender, EventArgs e)
        {
            try
            {
                var result = CommandManagement.ShowRateLimitDetails();

                AppendLineToOutput(result, Color.Black);
            }
            catch (Exception ex)
            {
                AppendLineToOutput("Error : " + ex.Message);
                AppendLineToOutput(ex.StackTrace);
            }

        }

        private void ButtonLookUpUsers_Click(object sender, EventArgs e)
        {
            CommandResult result = CommandResult.Failure;

            DisableUserLookUpControls();

            BackgroundWorker bgw = new BackgroundWorker();
            bgw.DoWork += delegate
            {
                AppendLineToOutput(string.Format("Starting task [{0}]", "Add Users by Id or Screenname"), Color.DarkGreen);

                object[] list = new object[0];
                ListType listType = ListType.Unknown;

                if (RadioById.Checked)
                {
                    listType = ListType.Ids;
                    List<object> validIds = new List<object>();
                    decimal tmp = -1;

                    foreach (var StringId in TextBoxInput.Lines)
                    {
                        decimal.TryParse(StringId, out tmp);

                        if (tmp > 0)
                        {
                            validIds.Add(tmp);
                        }
                    }

                    list = validIds.ToArray();

                    AppendLineToOutput(string.Format("{0} ids to be processed out of {1} lines given.", validIds.Count, list.Length), Color.DarkGreen);

                }
                else if (RadioByScreenname.Checked)
                {
                    listType = ListType.Screennames;
                    list = TextBoxInput.Lines;
                }

                result = CommandManagement.LookUpUsers(list, listType);
            };

            bgw.RunWorkerCompleted += delegate
            {
                AppendLineToOutput(result, Color.DarkGreen);
                AppendLineToOutput(string.Format("Completed task [{0}]", "Add Users by Id"), Color.DarkGreen);

                EnableUserLookUpControls();
            };

            bgw.RunWorkerAsync();
        }

        private void ButtonGetTimelinesById_Click(object sender, EventArgs e)
        {
            CommandResult result = CommandResult.Failure;

            DisableTimelineControls();

            BackgroundWorker bgw = new BackgroundWorker();
            bgw.DoWork += delegate
            {
                AppendLineToOutput(string.Format("Starting task [{0}]", "Get Timelines by Id"), Color.DarkSlateBlue);

                decimal[] ids = new decimal[TextBoxInput.Lines.Length];
                decimal tmp = -1;
                int i = 0;

                foreach (var StringId in TextBoxInput.Lines)
                {
                    decimal.TryParse(StringId, out tmp);

                    if (tmp > 0)
                    {
                        ids[i] = tmp;
                        i++;
                    }
                }
                AppendLineToOutput(string.Format("{0} ids to be processed out of {1} lines given.", i, ids.Length), Color.DarkSlateBlue);

                result = CommandManagement.GetTimelines(ids, CheckResumeTimeline.Checked);
            };

            bgw.RunWorkerCompleted += delegate
            {
                AppendLineToOutput(result, Color.Black);
                AppendLineToOutput(string.Format("Completed task [{0}]", "Get Timelines by Id"), Color.DarkSlateBlue);

                EnableTimelineControls();
            };

            bgw.RunWorkerAsync();
        }

        private void DisableTimelineControls()
        {
            ButtonGetTimelinesById.Enabled = false;
            TextBoxInput.ReadOnly = true;
            pictureBox3.BringToFront();
            pictureBox3.Visible = true;
        }
        private void EnableTimelineControls()
        {
            ButtonGetTimelinesById.Enabled = true;
            TextBoxInput.ReadOnly = false;
            pictureBox3.Visible = false;
        }

        //private PictureBox Anim = new PictureBox();
        private void DisableUserLookUpControls()
        {
            ButtonLookUpUsers.Enabled = false;
            TextBoxInput.ReadOnly = true;

            pictureBox2.BringToFront();
            pictureBox2.Visible = true;
        }

        private void EnableUserLookUpControls()
        {
            ButtonLookUpUsers.Enabled = true;
            TextBoxInput.ReadOnly = false;
            pictureBox2.Visible = false;

            //this.Controls.Remove(Anim);
        }

        private void DisableFollowerControls()
        {
            ButtonGetFollowerList.Enabled = false;
            RadioAirNZ.Enabled = false;
            RadioJetStar.Enabled = false;
            RadioQantas.Enabled = false;
            RadioVirginAustralia.Enabled = false;
            pictureBox1.Visible = true;
        }

        private void EnableFollowerControls()
        {
            ButtonGetFollowerList.Enabled = true;
            RadioAirNZ.Enabled = true;
            RadioJetStar.Enabled = true;
            RadioQantas.Enabled = true;
            RadioVirginAustralia.Enabled = true;
            pictureBox1.Visible = false;
        }


        private void button1_Click(object sender, EventArgs e)
        {
            try
            {
                List<decimal> tmp = new List<decimal> { 19273963, 19617105 };

                //TwitterResponse<UserIdCollection> Response = TwitterFriendship.FollowersIds(Tokens, new UsersIdsOptions { ScreenName = "FlyAirNZ", Cursor = -1 });
                //var Response = TwitterUser.Lookup(Tokens, new LookupUsersOptions() { UserIds = new TwitterIdCollection(tmp) });
                var Response = TwitterTimeline.UserTimeline(
                            Tokens,
                            new UserTimelineOptions
                            {
                                UserId = 19273963,
                                Count = 200,
                                //SkipUser = false, //get the user's full details so it matches the User object in our db context.
                                SkipUser = true,
                                IncludeRetweets = true
                            }
                            );

                var rate = Response.RateLimiting;

                AppendLineToOutput(string.Format("{0} out of {1} calls remaining ; resets at {2} ({3})"
                    , rate.Remaining
                    , rate.Total
                    , rate.ResetDate.ToLocalTime()
                    , rate.ResetDate.Subtract(DateTime.UtcNow).TotalMinutes.ToString()
                    ));
            }
            catch (Exception ex)
            {
                AppendLineToOutput("Error : " + ex.Message);
                AppendLineToOutput(ex.StackTrace);
            }
        }

        private void toolStripMenuItem1_Click(object sender, EventArgs e)
        {
            this.Icon = (Icon)Resources.ResourceManager.GetObject("Hamid");
        }

        private void pictureBox1_Click(object sender, EventArgs e)
        {

        }

        private void contextMenuStrip1_Opening(object sender, CancelEventArgs e)
        {

        }

        private void toolStripMenuItemClearText_Click(object sender, EventArgs e)
        {
            TextBoxInput.Clear();
        }

        private void toolStripMenuItem2_Click(object sender, EventArgs e)
        {
            RadioAirNZ.Checked = false;
            RadioJetStar.Checked = false;
            RadioQantas.Checked = false;
            RadioVirginAustralia.Checked = false;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            BackgroundWorker bgw = new BackgroundWorker();
            bgw.DoWork += delegate
            {
                //string screenname = "FlyAirNZ";

                //TwitterResponse<TwitterSearchResultCollection> searchResult = TwitterSearch.Search(
                //      Tokens
                //    , string.Format("to%3A{0}%20%40{0}", screenname)
                //    , new SearchOptions
                //    {
                //        SinceId = 0, //set to airline's earliest tweet from 3200 timeline
                //        ResultType = SearchOptionsResultType.Recent,
                //        Count = 100
                //    });

                //foreach (TwitterStatus tweet in searchResult.ResponseObject)
                //{
                //    //do stuff for each tweet - save this somewhere in the db for analysis?
                //    //just need to record unique ids (mark if they are followers or not)
                //}

            };

            bgw.RunWorkerCompleted += delegate
            {
                AppendLineToOutput(string.Format("Completed task [{0}]", "testing"), Color.DarkSlateBlue);
            };

            bgw.RunWorkerAsync();
        }

        private void ButtonSearchTweets_Click(object sender, EventArgs e)
        {
            CommandResult result = CommandResult.Failure;

            DisableSearchControls();

            BackgroundWorker bgw = new BackgroundWorker();
            bgw.DoWork += delegate
            {
                AppendLineToOutput(string.Format("Starting task [{0}]", "Search tweets to or mentioning user"), Color.DarkSlateBlue);

                decimal[] ids = new decimal[TextBoxInput.Lines.Length];
                decimal tmp = -1;
                int i = 0;

                foreach (var StringId in TextBoxInput.Lines)
                {
                    decimal.TryParse(StringId, out tmp);

                    if (tmp > 0)
                    {
                        ids[i] = tmp;
                        i++;
                    }
                }
                AppendLineToOutput(string.Format("{0} ids to be processed out of {1} lines given.", i, ids.Length), Color.DarkSlateBlue);

                result = CommandManagement.SearchTweets(ids);
            };

            bgw.RunWorkerCompleted += delegate
            {
                AppendLineToOutput(result, Color.Black);
                AppendLineToOutput(string.Format("Completed task [{0}]", "Search tweets to or mentioning user"), Color.DarkSlateBlue);

                EnableSearchControls();
            };

            bgw.RunWorkerAsync();
        }

        private void EnableSearchControls()
        {
            ButtonSearchTweets.Enabled = true;
            TextBoxInput.ReadOnly = false;
            //pictureBox4.BringToFront();
            pictureBox4.Visible = false;
        }

        private void DisableSearchControls()
        {
            ButtonSearchTweets.Enabled = false;
            TextBoxInput.ReadOnly = true;
            pictureBox4.BringToFront();
            pictureBox4.Visible = true;
        }

        private void button3_Click(object sender, EventArgs e)
        {
            CommandResult result = CommandResult.Failure;

            DisableReplyControls();

            BackgroundWorker bgw = new BackgroundWorker();
            bgw.DoWork += delegate
            {
                AppendLineToOutput(string.Format("Starting task [{0}]", "Get Tweets for InReplyToIds"), Color.DarkMagenta);

                decimal[] ids = new decimal[TextBoxInput.Lines.Length];
                decimal tmp = -1;
                int i = 0;

                foreach (var StringId in TextBoxInput.Lines)
                {
                    decimal.TryParse(StringId, out tmp);

                    if (tmp > 0)
                    {
                        ids[i] = tmp;
                        i++;
                    }
                }
                AppendLineToOutput(string.Format("{0} ids to be processed out of {1} lines given.", i, ids.Length), Color.DarkMagenta);

                result = CommandManagement.GetRepliedToTweets(ids);
            };

            bgw.RunWorkerCompleted += delegate
            {
                AppendLineToOutput(result, Color.Black);
                AppendLineToOutput(string.Format("Completed task [{0}]", "Get Tweets for InReplyToIds"), Color.DarkMagenta);

                EnableReplyControls();
            };

            bgw.RunWorkerAsync();
        }

        private void EnableReplyControls()
        {
            ButtonGetRepliedToTweets.Enabled = true;
            TextBoxInput.ReadOnly = false;
            //pictureBox4.BringToFront();
            pictureBox5.Visible = false;
        }

        private void DisableReplyControls()
        {

            ButtonGetRepliedToTweets.Enabled = false;
            TextBoxInput.ReadOnly = true;
            pictureBox5.BringToFront();
            pictureBox5.Visible = true;
        }


    }

}



