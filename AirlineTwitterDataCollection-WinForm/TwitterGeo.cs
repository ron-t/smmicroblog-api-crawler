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
    
    public partial class TwitterGeo
    {
        public TwitterGeo()
        {
            this.Coordinates = new HashSet<Coordinate>();
            this.TwitterStatus = new HashSet<TwitterStatu>();
        }
    
        public int Id { get; set; }
        public int ShapeType { get; set; }
    
        public virtual ICollection<Coordinate> Coordinates { get; set; }
        public virtual ICollection<TwitterStatu> TwitterStatus { get; set; }
    }
}
