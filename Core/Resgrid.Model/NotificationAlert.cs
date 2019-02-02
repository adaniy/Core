﻿using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("NotificationAlerts")]
	public class NotificationAlert: IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int NotificationAlertId { get; set; }

		[Required]
		[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[ForeignKey("Group"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int? DepartmentGroupId { get; set; }

		public virtual DepartmentGroup Group { get; set; }

		public int EventType { get; set; }

		public DateTime Opened { get; set; }

		public DateTime? Closed { get; set; }

		public bool ManuallyClosed { get; set; }

		public string Data { get; set; }

		public string ManualNote { get; set; }

		[NotMapped]
		public object Id
		{
			get { return NotificationAlertId; }
			set { NotificationAlertId = (int)value; }
		}
	}
}