using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity;
using System.Linq;
using Twitterizer;
using Twitterizer.Entities;


namespace AirlineTwitterDataCollection
{
    class EntityMangement
    {


    }

    public class TwitterContext : DbContext
    {
        public DbSet<TwitterUser> Users { get; set; }
        public DbSet<TwitterStatus> Tweets { get; set; }
        public DbSet<Friendship> Friendships { get; set; }

        public DbSet<SearchResult> SearchResults { get; set; }

        public TwitterContext()
        {
            this.Database.Connection.ConnectionString = @"Data Source=xxx;Initial Catalog=TwitterDataCollection";
            ((System.Data.Entity.Infrastructure.IObjectContextAdapter)this).ObjectContext.CommandTimeout = 300;
        }


        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("new");

            //set all decimal IDs to 20digits
            modelBuilder.Entity<TwitterStatus>().Property(x => x.Id).HasPrecision(20, 0);
            modelBuilder.Entity<TwitterStatus>().Property(x => x.InReplyToUserId).HasPrecision(20, 0);
            modelBuilder.Entity<TwitterStatus>().Property(x => x.InReplyToStatusId).HasPrecision(20, 0);
            modelBuilder.Entity<TwitterStatus>().Property(x => x.QuotedStatusId).HasPrecision(20, 0);

            modelBuilder.Entity<Friendship>().Property(x => x.UserId).HasPrecision(20, 0);
            modelBuilder.Entity<Friendship>().Property(x => x.FollowerId).HasPrecision(20, 0);

            modelBuilder.Entity<TwitterUser>().Property(x => x.Id).HasPrecision(20, 0);

            modelBuilder.Entity<TwitterMentionEntity>().Property(x => x.UserId).HasPrecision(20, 0);

            modelBuilder.Entity<TwitterMediaEntity>().Property(x => x.Id).HasPrecision(20, 0);
            

            modelBuilder.Entity<SearchResult>().Property(x => x.ForAirlineId).HasPrecision(20, 0);
            modelBuilder.Entity<SearchResult>().Property(x => x.PosterUserId).HasPrecision(20, 0);
            modelBuilder.Entity<SearchResult>().Property(x => x.TweetId).HasPrecision(20, 0);

            modelBuilder.Entity<TwitterUser>().HasOptional(u => u.Status)
                .WithOptionalPrincipal(s => s.User);

            modelBuilder.Entity<TwitterStatus>().HasOptional(t => t.RetweetedStatus)
                .WithMany();
        }
    }

    public class Friendship
    {
        [Key, Column(Order = 1)]
        public decimal UserId { get; set; }

        [Key, Column(Order = 2)]
        public decimal FollowerId { get; set; }

        [Key, Column(Order = 3)]
        public System.DateTime WhenSaved { get; set; }
    }

    public class SearchResult
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Key]
        public System.Guid Id { get; set; }
        public decimal ForAirlineId { get; set; }
        
        [StringLength(128)]
        public string ForAirlineScreenname { get; set; }
        public decimal TweetId { get; set; }
        public System.DateTime CreatedDate { get; set; }
        public decimal PosterUserId { get; set; }

        [StringLength(128)]
        public string PosterScreenname { get; set; }
        public string TweetText { get; set; }
        public bool IsTweetToAirline { get; set; }
        //where tweet.InReplyToUserId == idNumber
        //IsTweetToAirline includes replies: replies are where InReplyToStatus != null
        public bool IsRetweetOfAirline { get; set; }
        //where RetweetedStatus != null && RetweetedStatus.UserId == idNumber
        public bool IsMentionAirline { get; set; }
        //IsMention is else case (includes RTs about other users mentioning the airline)
    }

}
