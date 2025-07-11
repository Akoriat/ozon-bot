using System;
using System.ComponentModel.DataAnnotations;

namespace DAL.Models
{
    public class Review
    {
        [Key]
        public int Id { get; set; }
        public DateOnly ReviewDate { get; set; }
        public TimeOnly ReviewTime { get; set; }
        public string Status { get; set; }         
        public string Product { get; set; }        
        public string ReceptionStatus { get; set; }
        public string ReviewText { get; set; }     
        public int Rating { get; set; }
        public string Photo { get; set; }
        public string Video { get; set; }
        public string Answers { get; set; }
        public string Dialog { get; set; }            
        public string Name { get; set; }
        public string? Article { get; set; }
    }
}