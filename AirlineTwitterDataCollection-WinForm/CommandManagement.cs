using System;
using System.Data.Entity;
using System.Drawing;
using System.IO;
using System.Linq;
using Twitterizer;
using System.Collections.ObjectModel;
using System.Collections;
using System.Collections.Generic;

namespace AirlineTwitterDataCollection
{
    enum CommandResult { Success, Failure, NotInitiated }

    class CommandManagement
    {
        private const int NumberOfUsersPerLookup = 100;
        private const int MaxNumberOfTweets = 200;

        private static FormMain Form = null;
        private static OAuthTokens Tokens = null;
        private static bool IsInitiated = false;

        public static CommandResult Init(FormMain form, OAuthTokens tokens)
        {
            Form = form;
            Tokens = tokens;
            IsInitiated = true;

            if (Form == null)
            {
                IsInitiated = false;
            }

            if (Tokens == null)
            {
                IsInitiated = false;
            }

            return CommandResult.Success;
        }

        public static CommandResult LookUpUsers(object[] list, ListType listType)
        {
            return LookUpUsers(list, listType, NumberOfUsersPerLookup);
        }

        public static CommandResult LookUpUsers(object[] list, ListType listType, int UsersPerLookUp)
        {
            if (!IsInitiated)
            {
                return CommandResult.NotInitiated;
            }

            LookupUsersOptions lookupOptions = new LookupUsersOptions();

            //can lookup 100 ids at a time
            for (int i = 0; i < list.Length; i += UsersPerLookUp)
            {
                if (listType == ListType.Ids)
                {
                    lookupOptions.UserIds = new TwitterIdCollection(list.Skip(i).Take(UsersPerLookUp).Cast<decimal>().ToList<decimal>());

                }
                else if (listType == ListType.Screennames)
                {
                    lookupOptions.ScreenNames = new Collection<string>(list.Skip(i).Take(UsersPerLookUp).Cast<string>().ToList<string>());
                }

                TwitterResponse<TwitterUserCollection> UserLookupResponse = null;

                try
                {
                    UserLookupResponse = TwitterUser.Lookup(Tokens, lookupOptions);
                }
                catch (TwitterizerException)
                {
                    throw; //something is wrong with Twitterizer
                }
                catch (Exception ex)
                {
                    //do nothing (and manually try again later)
                    Form.AppendLineToOutput("Failed. " + ex.Message, Color.DarkGreen);
                }

                if (UserLookupResponse.Result == RequestResult.Success && UserLookupResponse.ResponseObject != null)
                {
                    //refactor this?
                    //SaveUsers(user, userLookupResponse.ResponseObject);

                    foreach (var user in UserLookupResponse.ResponseObject)
                    {
                        user.Status = null; //remove the user's latest tweet (this causes problems with FKs because
                        // the tweet's retweet, entities, places, etc. don't exist in the DB.
                    }
                    using (var scope = new System.Transactions.TransactionScope())
                    {
                        TwitterContext db = null;
                        try
                        {
                            db = new TwitterContext();
                            db.Configuration.AutoDetectChangesEnabled = false;
                            db.Configuration.ValidateOnSaveEnabled = false;

                            var LastUser = UserLookupResponse.ResponseObject.Last();

                            db.Users.AddRange(UserLookupResponse.ResponseObject);
                            db.SaveChanges(); //add up to 100 users

                            Form.AppendLineToOutput(string.Format("Added {0} users. Last user added is : {1} ({2})", UserLookupResponse.ResponseObject.Count, LastUser.ScreenName, LastUser.Id), Color.DarkGreen);
                        }
                        catch (System.Data.Entity.Infrastructure.DbUpdateException ex)
                        {
                            db = new TwitterContext();
                            db.Configuration.AutoDetectChangesEnabled = false;
                            db.Configuration.ValidateOnSaveEnabled = false;

                            Form.AppendLineToOutput(string.Format("Error with some users : {0}", ex.Message), Color.DarkGreen);
                            Form.AppendLineToOutput(string.Format("Continuing with remaining {0} users", list.Length - i), Color.DarkGreen);
                        }
                        catch (Exception ex)
                        {
                            Form.AppendLineToOutput(string.Format("Error : {0}", ex.Message), Color.DarkGreen);
                            //do nothing, really
                            TimeSpan _OneSecond = new TimeSpan(0, 0, 1);
                            System.Threading.Thread.Sleep(_OneSecond);

                            db = new TwitterContext(); //to clear the follower in the current context which caused the exception
                            db.Configuration.AutoDetectChangesEnabled = false;
                            db.Configuration.ValidateOnSaveEnabled = false;
                        }
                        finally
                        {
                            if (db != null)
                            {
                                db.Dispose();
                            }
                        }

                        scope.Complete();
                    }

                }
                else if (UserLookupResponse.Result == RequestResult.RateLimited)
                {
                    WaitForRateLimitReset(UserLookupResponse);
                    Form.AppendLineToOutput("Resuming LookUpUsers command", Color.DarkGreen);

                    continue;
                }
                else
                {
                    HandleTwitterizerError<TwitterUserCollection>(UserLookupResponse);
                }
            }

            return CommandResult.Success;
        }

        internal static CommandResult GetTimelines(decimal[] ids, bool resume)
        {
            if (!IsInitiated)
            {
                return CommandResult.NotInitiated;
            }

            foreach (var id in ids)
            {
                if (id == 0)
                    continue;

                var UserToProcess = (new TwitterContext()).Users.FirstOrDefault(u => u.Id == id);

                if (UserToProcess == null)
                {
                    Form.AppendLineToOutput(string.Format("User with id {0} does not exist in the database.", id), Color.DarkSlateBlue);
                    continue;
                }
                //else continue and get user's timeline

                string ScreenName = UserToProcess.ScreenName;
                decimal IdNumber = UserToProcess.Id;
                long NumberOfStatuses = UserToProcess.NumberOfStatuses;

                TwitterContext db = null;
                try
                {
                    //var _friendshipContext = new TwitterEntitiesForFriendship();

                    TwitterResponse<TwitterStatusCollection> TimelineResponse;
                    int numTweets = 0;
                    int duplicatesFoundTotal = 0;

                    Form.AppendLineToOutput(string.Format("Attempt to save timeline for {0} ({1} tweets).", ScreenName, NumberOfStatuses), Color.DarkSlateBlue);

                    decimal EarliestTweetId = 0;

                    if (resume)
                    {
                        EarliestTweetId = GetEarliestTweetId(id);
                    }
                    do
                    {
                        TimelineResponse = TwitterTimeline.UserTimeline(
                            Tokens,
                            new UserTimelineOptions
                                {
                                    UserId = IdNumber,
                                    Count = MaxNumberOfTweets,
                                    //SkipUser = false, //get the user's full details so it matches the User object in our db context.
                                    SkipUser = true,
                                    MaxStatusId = EarliestTweetId,
                                    IncludeRetweets = true
                                }
                            );

                        if (TimelineResponse.Result == RequestResult.Success)
                        {
                            TwitterStatusCollection tweets = TimelineResponse.ResponseObject;
                            int duplicates = 0;
                            if (tweets == null || tweets.Count == 0)
                            {
                                break;
                            }
                            else
                            {
                                foreach (TwitterStatus tweet in tweets)
                                {
                                    //refresh context
                                    db = new TwitterContext();
                                    db.Configuration.AutoDetectChangesEnabled = false;

                                    if (!db.Tweets.Any(t => t.Id == tweet.Id)) //only continue if the tweet doesn't already exist
                                    {
                                        SaveTweet(tweet);

                                        numTweets++;
                                    }
                                    else
                                    {
                                        duplicates++;;
                                    }
                                }

                                duplicatesFoundTotal += duplicates;
                                EarliestTweetId = tweets.LastOrDefault().Id - 1;
                            }

                            Form.AppendLineToOutput(string.Format("{0} tweets now saved for {1} (id:{2}). {3} duplicate {4} not saved.", numTweets, ScreenName, IdNumber, duplicatesFoundTotal, duplicatesFoundTotal == 1 ? "tweet" : "tweets"), Color.DarkSlateBlue);

                            //if we are getting no more new tweets then assume saved timeline is now up to date.
                            if (duplicates == tweets.Count)
                            {
                                break;
                            }
                        }
                        else if (TimelineResponse.Result == RequestResult.RateLimited)
                        {
                            WaitForRateLimitReset(TimelineResponse);

                            Form.AppendLineToOutput("Resuming GetTimelines command", Color.DarkSlateBlue);
                            continue;
                        }
                        else if (TimelineResponse.Result == RequestResult.Unauthorized || TimelineResponse.Result == RequestResult.FileNotFound)
                        {
                            /**
                             * Attempt to fix a bug discovered on 2012-06-21: user no longer exists so Twitter returns a 
                             * FileNotFound error ('sorry the page no longer exists'). Because of the hack above which 
                             * forces the loop to continue it keeps looping and getting the same error until all 350 calls
                             * are exhausted then repeats and repeats :(
                             * 
                             * Attempted fix/change is: added "|| timelineResponse.Result == RequestResult.FileNotFound"
                             * treat no-longer-existant users the same as protected users.
                             **/
                            Form.AppendLineToOutput(string.Format("User {0} is now private or no longer exists.", ScreenName), Color.DarkSlateBlue);

                            //Set user to protected.
                            using (var tmpDb = new TwitterContext())
                            {
                                var u = tmpDb.Users.Find(IdNumber);
                                u.IsProtected = true;
                                tmpDb.SaveChanges();
                            }

                            break; //give up with current user
                        }
                        else
                        {
                            HandleTwitterizerError<TwitterStatusCollection>(TimelineResponse);

                            //log that this user should be retried later
                            File.AppendAllText(@".\users-to-retry.txt", IdNumber.ToString() + Environment.NewLine);

                            break; //give up with current user for now
                        }

                    } while (TimelineResponse.ResponseObject != null && TimelineResponse.ResponseObject.Count > 0);

                }
                catch (Exception e)
                {
                    Exception current = e;
                    Form.AppendLineToOutput(string.Format("Unexpected exception : {0}", e.Message), Color.DarkSlateBlue);

                    while (current.InnerException != null)
                    {
                        Form.AppendLineToOutput(string.Format("Inner exception : {0}", current.InnerException.Message), Color.DarkSlateBlue);
                        current = current.InnerException;
                    }

                    //log that this user should be retried later
                    File.AppendAllText(@".\users-to-retry.txt", IdNumber.ToString() + Environment.NewLine);

                    continue; //give up with current user
                }
                finally
                {
                    if (db != null)
                        db.Dispose();
                }

            }

            return CommandResult.Success;
        }

        public static CommandResult GetFollowerList(string[] screennames)
        {
            if (!IsInitiated)
            {
                return CommandResult.NotInitiated;
            }

            foreach (var screenname in screennames)
            {
                var user = CheckUserExistsInDb(screenname);
                if (user == null)
                {
                    Form.AppendLineToOutput("Failed. @" + screenname + " not found in Db", Color.Maroon);
                    continue;
                }

                DateTime WhenSaved = DateTime.Today;
                int FollowersSaved = 0;
                int NotSaved = 0;
                long NextCursor = -1;

                #region log to file for resuming
                var logFilePath = @"./" + screenname + "-GetFollowerList.resume";

                if (File.Exists(logFilePath))
                {
                    var NextCursorAsText = File.ReadAllText(logFilePath);
                    long.TryParse(NextCursorAsText, out NextCursor);
                }
                #endregion

                do
                {
                    var db = new TwitterContext();
                    db.Configuration.AutoDetectChangesEnabled = false;

                    TwitterResponse<UserIdCollection> Response = TwitterFriendship.FollowersIds(Tokens, new UsersIdsOptions { ScreenName = screenname, Cursor = NextCursor });

                    if (Response.Result == RequestResult.Success && Response.ResponseObject != null)
                    {
                        File.WriteAllText(logFilePath, NextCursor.ToString()); //to note where to resume from

                        try
                        {
                            var followersID = Response.ResponseObject;
                            NextCursor = followersID.NextCursor;

                            foreach (var id in followersID.ToList())
                            {
                                try
                                {
                                    db.Friendships.Add(new Friendship { UserId = user.Id, FollowerId = id, WhenSaved = WhenSaved });
                                    FollowersSaved++;
                                    db.SaveChanges();
                                }
                                catch (Exception ex)
                                {
                                    Form.AppendLineToOutput(string.Format("Unexpected error with user {0} and follower {1} : {2}", user.ScreenName, id, ex.Message), Color.Maroon);
                                    db = new TwitterContext();
                                    NotSaved++;
                                }
                            }
                            Form.AppendLineToOutput(string.Format("Saved {0} followers so far; {1} not saved", FollowersSaved, NotSaved), Color.Maroon);
                        }
                        catch (Exception ex)
                        {
                            Form.AppendLineToOutput("Unexpected error: " + ex.Message);
                        }
                    }
                    else if (Response.Result == RequestResult.Unauthorized || Response.Result == RequestResult.FileNotFound)
                    {
                        Form.AppendLineToOutput("User " + user.ScreenName + " is now protected or no longer exists.", Color.Maroon);

                    }
                    else if (Response.Result == RequestResult.RateLimited)
                    {
                        WaitForRateLimitReset(Response);

                        Form.AppendLineToOutput("Resuming GetFollowerList command", Color.Maroon);
                        continue;
                    }
                    else //Just in case if the program is unexpectedly failed and closed, record last processed page number
                    {
                        Form.AppendLineToOutput("Error: " + Response.Result, Color.Maroon);

                        //StreamWriter w1 = new StreamWriter("SaveFollowingsList_PageSoFar.txt");
                        //w1.WriteLine("Record datetime: " + DateTime.Now);
                        //w1.WriteLine("Page number next to be: " + nextCursor);
                        //w1.Close();
                        //Console.WriteLine("End of Process");

                        break;
                    }
                } while (NextCursor > 0);

                if (File.Exists(logFilePath))
                {
                    File.Delete(logFilePath); //remove log file. We're done with this user now.
                }
            } //end loop for each screenname

            return CommandResult.Success;
        }

        public static CommandResult SearchTweets(decimal[] ids)
        {
            if (!IsInitiated)
            {
                return CommandResult.NotInitiated;
            }

            foreach (var id in ids)
            {
                if (id == 0)
                    continue;

                var UserToProcess = (new TwitterContext()).Users.FirstOrDefault(u => u.Id == id);

                if (UserToProcess == null)
                {
                    Form.AppendLineToOutput(string.Format("User with id {0} does not exist in the database.", id), Color.Olive);
                    continue;
                }
                //else continue and search for tweets to the user and mentions of the user

                string screenname = UserToProcess.ScreenName;
                decimal idNumber = UserToProcess.Id;

                TwitterContext db = null;
                try
                {
                    TwitterResponse<TwitterSearchResultCollection> searchResult;
                    int numTweets = 0;
                    int duplicatesFound = 0;

                    Form.AppendLineToOutput(string.Format("Attempt to search for tweets to and mentioning {0} in the last 4 months", screenname), Color.Olive);

                    //continue from SearchResult if there is tweet, otherwise use user's latest tweet id
                    //decimal latestTweetId = GetEarliestTweetIdFromSearchResult(id);
                    //if(latestTweetId == 0)
                    //{
                    //    latestTweetId = GetLatestTweetId(id);
                    //}

                    //decimal earliestTweetId = GetEarliestTweetId(id);


                    decimal latestTweetId = 701646604403478528;
                    //701646604403478528 : id of a tweet dated 21 Feb 2016 9:56 p.m.

                    decimal earliestTweetId = 656323760102879232;
                    //656323760102879232 : id of a tweet dated 19 Oct 2015 9:19 p.m.

                    do
                    {
                        searchResult = TwitterSearch.Search(
                            Tokens,
                            string.Format("to%3A{0}%20%40{0}", screenname),
                            new SearchOptions
                            {
                                MaxId = latestTweetId - 1,
                                SinceId = earliestTweetId,
                                ResultType = SearchOptionsResultType.Recent,
                                Count = 100
                            });

                        if (searchResult.Result == RequestResult.Success)
                        {
                            TwitterSearchResultCollection tweets = searchResult.ResponseObject;

                            if (tweets == null || tweets.Count == 0)
                            {
                                break;
                            }
                            else
                            {
                                //refresh context
                                db = new TwitterContext();
                                db.Configuration.AutoDetectChangesEnabled = false;

                                decimal earliestIdFromSearch = latestTweetId;

                                foreach (TwitterStatus tweet in tweets)
                                {
                                    SearchResult sr = new SearchResult();

                                    sr.ForAirlineId = idNumber;
                                    sr.ForAirlineScreenname = screenname;

                                    sr.TweetId = tweet.Id;
                                    sr.CreatedDate = tweet.CreatedDate;
                                    sr.PosterUserId = tweet.User.Id;
                                    sr.PosterScreenname = tweet.User.ScreenName;
                                    sr.TweetText = tweet.Text;

                                    sr.IsTweetToAirline = (tweet.InReplyToUserId == idNumber);
                                    sr.IsRetweetOfAirline = (tweet.RetweetedStatus != null && tweet.RetweetedStatus.User.Id == idNumber);
                                    sr.IsMentionAirline = (!sr.IsTweetToAirline && !sr.IsRetweetOfAirline);

                                    if (tweet.Id < earliestIdFromSearch)
                                    {
                                        earliestIdFromSearch = tweet.Id;
                                    }

                                    numTweets++;

                                    db.SearchResults.Add(sr);
                                }

                                try
                                {
                                    db.SaveChanges();
                                }
                                catch (Exception ex)
                                {
                                    Form.AppendLineToOutput(ex.Message, Color.Olive);
                                    Form.AppendLineToOutput(ex.StackTrace, Color.Olive);
                                    return CommandResult.Failure;
                                }

                                latestTweetId = earliestIdFromSearch;
                            }

                            Form.AppendLineToOutput(string.Format("{0} tweets processed for {1} (id:{2}). {3} duplicate {4} not saved.", numTweets, screenname, idNumber, duplicatesFound, duplicatesFound == 1 ? "tweet" : "tweets"), Color.Olive);
                        }
                        else if (searchResult.Result == RequestResult.RateLimited)
                        {
                            WaitForRateLimitReset(searchResult);

                            Form.AppendLineToOutput("Resuming search tweets command", Color.Olive);
                            continue;
                        }
                        else
                        {
                            HandleTwitterizerError<TwitterSearchResultCollection>(searchResult);

                            //log that this user should be retried later
                            File.AppendAllText(@".\users-to-retry.txt", idNumber.ToString() + Environment.NewLine);

                            break; //give up with current user for now
                        }

                    } while (searchResult.ResponseObject != null && searchResult.ResponseObject.Count > 0);

                }
                catch (Exception e)
                {
                    Exception current = e;
                    Form.AppendLineToOutput(string.Format("Unexpected exception : {0}", e.Message), Color.Olive);

                    while (current.InnerException != null)
                    {
                        Form.AppendLineToOutput(string.Format("Inner exception : {0}", current.InnerException.Message), Color.Olive);
                        current = current.InnerException;
                    }

                    //log that this user should be retried later
                    File.AppendAllText(@".\users-to-retry.txt", idNumber.ToString() + Environment.NewLine);

                    continue; //give up with current user
                }
                finally
                {
                    if (db != null)
                        db.Dispose();
                }

            }

            return CommandResult.Success;
        }

        public static CommandResult GetRepliedToTweets(decimal[] ids)
        {
            if (!IsInitiated)
            {
                return CommandResult.NotInitiated;
            }

            Form.AppendLineToOutput(string.Format("Attempt to get tweets which were replied to"), Color.DarkMagenta);

            int numTweets = 0;
            int notAdded = 0;

            for (int i = 0; i < ids.Length; i += 100)
            {
                TwitterIdCollection idsToLookup = new TwitterIdCollection(ids.Skip(i).Take(100).ToList());

                TwitterContext db = null;
                try
                {
                    TwitterResponse<TwitterStatusCollection> result = null;

                    result = TwitterStatus.Lookup(Tokens, new LookupStatusesOptions
                    {
                        StatusIds = idsToLookup
                    });

                    if (result.Result == RequestResult.Success)
                    {
                        TwitterStatusCollection tweets = result.ResponseObject;

                        //refresh context
                        db = new TwitterContext();
                        db.Configuration.AutoDetectChangesEnabled = false;

                        foreach (TwitterStatus tweet in tweets)
                        {
                            try
                            {
                                numTweets++;

                                TwitterUser userInDb = db.Users.FirstOrDefault(u => u.Id == tweet.User.Id);
                                if (userInDb != null)
                                {
                                    tweet.User = userInDb;
                                }

                                tweet.RetweetedStatus = null;
                                tweet.QuotedStatus = null;

                                db.Tweets.Add(tweet);
                                db.SaveChanges();
                            }
                            catch (Exception ex)
                            {

                                //Form.AppendLineToOutput(string.Format("{0} -ERROR- {1}", tweet.Id, ex.Message), Color.DarkMagenta);
                                //Form.AppendLineToOutput(ex.StackTrace, Color.DarkMagenta);

                                db = new TwitterContext();
                                notAdded += 1;
                            }
                        }



                        Form.AppendLineToOutput(string.Format("{0} tweets processed. {1} duplicate {2} not saved.", numTweets, notAdded, notAdded == 1 ? "tweet" : "tweets"), Color.DarkMagenta);
                    }
                    else if (result.Result == RequestResult.RateLimited)
                    {
                        WaitForRateLimitReset(result);

                        Form.AppendLineToOutput("Resuming get replied-to-tweets command", Color.DarkMagenta);
                        continue;
                    }
                }
                catch (Exception e)
                {
                    Exception current = e;
                    Form.AppendLineToOutput(string.Format("Unexpected exception : {0}", e.Message), Color.DarkMagenta);

                    while (current.InnerException != null)
                    {
                        Form.AppendLineToOutput(string.Format("Inner exception : {0}", current.InnerException.Message), Color.DarkMagenta);
                        current = current.InnerException;
                    }

                    db = new TwitterContext();

                    continue; //give up with current batch
                }
                finally
                {
                    if (db != null)
                        db.Dispose();
                }

            }

            return CommandResult.Success;
        }

        private static TwitterUser CheckUserExistsInDb(string screenname)
        {
            using (var db = new TwitterContext())
            {
                return db.Users.FirstOrDefault(u => u.ScreenName.Equals(screenname));
            }
        }

        private static decimal GetEarliestTweetId(decimal userId)
        {
            return (from t in (new TwitterContext()).Tweets
                    where t.User.Id == userId && t.CreatedDate > new DateTime(2016, 01, 01) //temp fix 2016-02-23
                    orderby t.Id
                    select t.Id).FirstOrDefault();
        }

        private static decimal GetLatestTweetId(decimal userId)
        {
            return (from t in (new TwitterContext()).Tweets
                    where t.User.Id == userId
                    orderby t.Id descending
                    select t.Id).FirstOrDefault();
        }

        private static decimal GetEarliestTweetIdFromSearchResult(decimal userId)
        {
            return (from t in (new TwitterContext()).SearchResults
                    where t.ForAirlineId == userId
                    orderby t.TweetId
                    select t.TweetId).FirstOrDefault();
        }

        private static void SaveTweet(TwitterStatus tweet)
        {
            var newDb = new TwitterContext();
            if (newDb.Users.Any(u => u.Id == tweet.User.Id))
            {
                newDb.Entry(tweet.User).State = EntityState.Unchanged;
            }

            //don't save the place
            tweet.Place = null;

            //don't save the geo
            tweet.Geo = null;

            if (tweet.QuotedStatus != null)
            {
                //SaveTweet(db, tweet.QuotedStatus); don't save the quoted status, just the id

                tweet.QuotedStatusId = tweet.QuotedStatus.Id;
                tweet.QuotedStatus = null;

            }

            bool RetweetExists = false;
            if (tweet.RetweetedStatus != null)
            {
                //SaveTweet(db, tweet.RetweetedStatus); don't save the retweeted status, just the id

                tweet.RetweetedStatus = new TwitterStatus { Id = tweet.RetweetedStatus.Id, CreatedDate = tweet.RetweetedStatus.CreatedDate };

                if (newDb.Tweets.Any(t => t.Id == tweet.RetweetedStatus.Id))
                {
                    newDb.Entry(tweet.RetweetedStatus).State = EntityState.Unchanged;
                    RetweetExists = true;
                }
            }

            newDb.Tweets.Add(tweet);
            newDb.SaveChanges();

            //saving the retweeted status id creates a dummy status. So we remove it afterward if it was added
            if (!RetweetExists && tweet.RetweetedStatus != null)
            {
                newDb = new TwitterContext(); //to prevent setting tweet.retweedstatusid to null
                newDb.Entry(tweet.RetweetedStatus).State = EntityState.Deleted;
                newDb.SaveChanges();
            }
        }

        private static void WaitForRateLimitReset<T>(TwitterResponse<T> Response) where T : Twitterizer.Core.ITwitterObject
        {
            var WaitTime = Response.RateLimiting.ResetDate.Subtract(DateTime.UtcNow);
            WaitTime = WaitTime.Add(new TimeSpan(0, 0, 90)); //add 90 seconds to waittime to account for time difference (I don't know why the resetdate time is off =\).

            if (WaitTime.TotalMinutes < 0)
            {
                WaitTime = WaitTime.Negate();
            }

            Form.AppendLineToOutput(
                string.Format("RATE LIMIT REACHED. Waiting for {0} minutes before rate limit resets at {1}."
                    , WaitTime.TotalMinutes
                    , Response.RateLimiting.ResetDate.ToLocalTime()));

            System.Threading.Thread.Sleep(WaitTime);
        }

        public static void HandleTwitterizerError<T>(TwitterResponse<T> response) where T : Twitterizer.Core.ITwitterObject
        {
            // Something bad happened, time to figure it out.
            string RawDataReturnedByTwitter = response.Content;
            string ErrorMessageReturnedByTwitter = response.ErrorMessage;
            if (string.IsNullOrEmpty(ErrorMessageReturnedByTwitter))
            {
                ErrorMessageReturnedByTwitter = "No error given";
            }
            var ErrorMessage = "Error from twitter: " + ErrorMessageReturnedByTwitter + Environment.NewLine;
            // The possible reasons something went wrong
            switch (response.Result)
            {
                case RequestResult.FileNotFound:
                    ErrorMessage += "\t This usually means the user doesn't exist.";
                    break;
                case RequestResult.BadRequest:
                    ErrorMessage += "\t An unknown error occurred (RequestResult = BadRequest).";
                    break;
                case RequestResult.Unauthorized:
                    ErrorMessage += "\t An unknown error occurred (RequestResult = Unauthorized).";
                    break;
                case RequestResult.NotAcceptable:
                    ErrorMessage += "\t An unknown error occurred (RequestResult = NotAcceptable).";
                    break;
                case RequestResult.RateLimited:
                    TimeSpan ttr = DateTime.Now.ToUniversalTime().Subtract(response.RateLimiting.ResetDate).Duration();
                    ErrorMessage += "Rate limit of " + response.RateLimiting.Total + " reached; ";
                    break;
                case RequestResult.TwitterIsDown:
                    //
                    break;
                case RequestResult.TwitterIsOverloaded:
                    ErrorMessage += "\t Twitter is overloaded (or down)";
                    //System.Threading.Thread.Sleep(_fiveSec);
                    break;
                case RequestResult.ConnectionFailure:
                    ErrorMessage += "\t An unknown error occurred (RequestResult = ConnectionFailure).";
                    break;
                case RequestResult.Unknown:
                    ErrorMessage += "\t An unknown error occurred (RequestResult = Unknown).";
                    break;
                default:
                    ErrorMessage += "\t An unknown error occurred.)";
                    break;
            }

            Form.AppendLineToOutput(ErrorMessage);
        }

        internal static CommandResult ShowRateLimitDetails()
        {
            if (!IsInitiated)
            {
                return CommandResult.NotInitiated;
            }

            var v = new VerifyCredentialsOptions();
            v.UseSSL = true;

            var Response = TwitterAccount.VerifyCredentials(Tokens, v);

            if (Response.Result == RequestResult.Success)
            {
                TwitterUser acc = Response.ResponseObject;
                RateLimiting status = Response.RateLimiting;

                Form.AppendLineToOutput("Screenname     : " + acc.ScreenName);
                Form.AppendLineToOutput("Hourly limit   : " + status.Total);
                Form.AppendLineToOutput("Remaining hits : " + status.Remaining);
                Form.AppendLineToOutput("Reset time     : " + status.ResetDate.ToLocalTime() + " (" + DateTime.Now.ToUniversalTime().Subtract(status.ResetDate).Duration().TotalMinutes + " mins left)");

                return CommandResult.Success;
            }
            else
            {
                HandleTwitterizerError<TwitterUser>(Response);
                return CommandResult.Failure;
            }
        }

    }
}
