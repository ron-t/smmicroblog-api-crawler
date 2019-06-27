//------------------------------------------------------------------------------
// <auto-generated>
//    This code was generated from a template.
//
//    Manual changes to this file may cause unexpected behavior in your application.
//    Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace AirlineTwitterDataCollection_WinForm
{
    using System;
    using System.Collections.Generic;
    
    public partial class TwitterStatu
    {
        public TwitterStatu()
        {
            this.TwitterEntities = new HashSet<TwitterEntity>();
            this.TwitterStatus1 = new HashSet<TwitterStatu>();
            this.TwitterStatus11 = new HashSet<TwitterStatu>();
        }
    
        public decimal Id { get; set; }
        public string StringId { get; set; }
        public Nullable<bool> IsTruncated { get; set; }
        public System.DateTime CreatedDate { get; set; }
        public string Source { get; set; }
        public string InReplyToScreenName { get; set; }
        public Nullable<decimal> InReplyToUserId { get; set; }
        public Nullable<decimal> InReplyToStatusId { get; set; }
        public Nullable<bool> IsFavorited { get; set; }
        public string FavoriteCountString { get; set; }
        public string Text { get; set; }
        public string RetweetCountString { get; set; }
        public bool Retweeted { get; set; }
        public Nullable<decimal> QuotedStatusId { get; set; }
        public string QuotedStatusStringId { get; set; }
        public Nullable<int> Geo_Id { get; set; }
        public string Place_Id { get; set; }
        public Nullable<decimal> RetweetedStatus_Id { get; set; }
        public Nullable<decimal> User_Id { get; set; }
    
        public virtual ICollection<TwitterEntity> TwitterEntities { get; set; }
        public virtual TwitterGeo TwitterGeo { get; set; }
        public virtual TwitterPlace TwitterPlace { get; set; }
        public virtual ICollection<TwitterStatu> TwitterStatus1 { get; set; }
        public virtual TwitterStatu TwitterStatu1 { get; set; }
        public virtual ICollection<TwitterStatu> TwitterStatus11 { get; set; }
        public virtual TwitterStatu TwitterStatu2 { get; set; }
        public virtual TwitterUser TwitterUser { get; set; }
    }
}