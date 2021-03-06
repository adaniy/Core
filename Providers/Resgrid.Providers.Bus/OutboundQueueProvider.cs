﻿using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Providers.Bus.Rabbit;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Providers.Bus
{
	public class OutboundQueueProvider : IOutboundQueueProvider
	{
		private readonly RabbitOutboundQueueProvider _rabbitOutboundQueueProvider;
		private static QueueClient _callClient = null;
		private static QueueClient _messageClient = null;
		private static QueueClient _notificationClient = null;
		private static QueueClient _shiftsClient = null;
		private static QueueClient _distributionListClient = null;

		public OutboundQueueProvider()
		{
			_rabbitOutboundQueueProvider = new RabbitOutboundQueueProvider();

			VerifyAndCreateClients();
		}

		public void EnqueueCall(CallQueueItem callQueue)
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
			{
				_rabbitOutboundQueueProvider.EnqueueCall(callQueue);
				return;
			}

			VerifyAndCreateClients();
			string serializedObject = String.Empty;

			try
			{
				serializedObject = ObjectSerialization.Serialize(callQueue);

				// We are limited to 256KB in azure queue messages
				var size = ASCIIEncoding.Unicode.GetByteCount(serializedObject);
				if (size > 220000)
				{
					callQueue.Profiles = null;
					serializedObject = ObjectSerialization.Serialize(callQueue);
				}
			}
			catch { }

			// If we get an Exception, i.e. OutOfMemmory, lets just strip out the heavy data and try.
			if (String.IsNullOrWhiteSpace(serializedObject))
			{
				callQueue.Profiles = null;
				serializedObject = ObjectSerialization.Serialize(callQueue);
			}

			BrokeredMessage message = new BrokeredMessage(serializedObject);
			message.MessageId = string.Format("{0}|{1}", callQueue.Call.CallId, callQueue.Call.DispatchCount);

			SendMessage(_callClient, message);

		}

		public void EnqueueMessage(MessageQueueItem messageQueue)
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
			{
				_rabbitOutboundQueueProvider.EnqueueMessage(messageQueue);
				return;
			}

			VerifyAndCreateClients();
			string serializedObject = String.Empty;

			if (messageQueue != null && messageQueue.Message != null && messageQueue.MessageId == 0 && messageQueue.Message.MessageId != 0)
				messageQueue.MessageId = messageQueue.Message.MessageId;

			try
			{
				serializedObject = ObjectSerialization.Serialize(messageQueue);

				// We are limited to 256KB in azure queue messages
				var size = ASCIIEncoding.Unicode.GetByteCount(serializedObject);
				if (size > 220000)
				{
					messageQueue.Profiles = null;
					serializedObject = ObjectSerialization.Serialize(messageQueue);
				}

				if (ASCIIEncoding.Unicode.GetByteCount(serializedObject) > 220000)
				{
					messageQueue.Message.MessageRecipients = null;
					serializedObject = ObjectSerialization.Serialize(messageQueue);
				}
			}
			catch { }

			// If we get an Exception, i.e. OutOfMemmory, lets just strip out the heavy data and try.
			if (String.IsNullOrWhiteSpace(serializedObject))
			{
				messageQueue.Profiles = null;
				messageQueue.Message.MessageRecipients = null;
				serializedObject = ObjectSerialization.Serialize(messageQueue);
			}

			BrokeredMessage message = new BrokeredMessage(serializedObject);
			message.MessageId = string.Format("{0}|{1}", messageQueue.Message.MessageId, messageQueue.Message.ReceivingUserId);

			SendMessage(_messageClient, message);
		}

		public void EnqueueDistributionList(DistributionListQueueItem distributionListQueue)
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
			{
				_rabbitOutboundQueueProvider.EnqueueDistributionList(distributionListQueue);
				return;
			}

			VerifyAndCreateClients();
			string serializedObject = String.Empty;

			try
			{
				serializedObject = ObjectSerialization.Serialize(distributionListQueue);

				// We are limited to 256KB in azure queue messages
				var size = ASCIIEncoding.Unicode.GetByteCount(serializedObject);
				if (size > 220000)
				{
					distributionListQueue.Users = null;
					serializedObject = ObjectSerialization.Serialize(distributionListQueue);
				}

				// If were still too big, strip out some attachments
				if (size > 220000)
				{
					distributionListQueue.Message.Attachments = null;
					serializedObject = ObjectSerialization.Serialize(distributionListQueue);
				}
			}
			catch { }

			// If we get an Exception, i.e. OutOfMemmory, lets just strip out the heavy data and try.
			if (String.IsNullOrWhiteSpace(serializedObject))
			{
				distributionListQueue.Users = null;
				distributionListQueue.Message.Attachments = null;
				serializedObject = ObjectSerialization.Serialize(distributionListQueue);
			}

			BrokeredMessage message = new BrokeredMessage(serializedObject);
			message.MessageId = string.Format("{0}|{1}", distributionListQueue.Message.MessageID, distributionListQueue.List.DistributionListId);

			SendMessage(_distributionListClient, message);
		}

		public void EnqueueNotification(NotificationItem notificationQueue)
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
			{
				_rabbitOutboundQueueProvider.EnqueueNotification(notificationQueue);
				return;
			}

			VerifyAndCreateClients();

			BrokeredMessage message = new BrokeredMessage(ObjectSerialization.Serialize(notificationQueue));
			message.MessageId = string.Format("{0}", notificationQueue.GetHashCode());

			SendMessage(_notificationClient, message);
		}

		public void EnqueueShiftNotification(ShiftQueueItem shiftQueueItem)
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
			{
				_rabbitOutboundQueueProvider.EnqueueShiftNotification(shiftQueueItem);
				return;
			}

			VerifyAndCreateClients();

			BrokeredMessage message = new BrokeredMessage(ObjectSerialization.Serialize(shiftQueueItem));
			message.MessageId = Guid.NewGuid().ToString();

			SendMessage(_shiftsClient, message);
		}

		private void SendMessage(QueueClient client, BrokeredMessage message)
		{
			if (client != null)
			{
#pragma warning disable 4014
				Task.Run(() =>
				{
					int retry = 0;
					bool sent = false;

					while (!sent)
					{
						try
						{
							client.Send(message);
							sent = true;
						}
						catch (Exception ex)
						{
							Logging.LogException(ex, message.ToString());

							if (retry >= 5)
								return false;

							Thread.Sleep(1000);
							retry++;
						}
					}

					return sent;
				}).ConfigureAwait(false);
#pragma warning restore 4014
			}
		}

		private void VerifyAndCreateClients()
		{
			if (!String.IsNullOrWhiteSpace(Config.ServiceBusConfig.AzureQueueConnectionString))
			{
				while (_callClient == null || _callClient.IsClosed)
				{
					try
					{
						var builder = new ServiceBusConnectionStringBuilder(Config.ServiceBusConfig.AzureQueueConnectionString)
						{
							OperationTimeout = TimeSpan.FromMinutes(5)
						};

						var messagingFactory = MessagingFactory.CreateFromConnectionString(builder.ToString());
						_callClient = messagingFactory.CreateQueueClient(Config.ServiceBusConfig.CallBroadcastQueueName);
					}
					catch (TimeoutException) { }
				}
			}

			if (!String.IsNullOrWhiteSpace(Config.ServiceBusConfig.AzureQueueMessageConnectionString))
			{
				while (_messageClient == null || _messageClient.IsClosed)
				{
					try
					{
						var builder = new ServiceBusConnectionStringBuilder(Config.ServiceBusConfig.AzureQueueMessageConnectionString)
						{
							OperationTimeout = TimeSpan.FromMinutes(5)
						};

						var messagingFactory = MessagingFactory.CreateFromConnectionString(builder.ToString());
						_messageClient = messagingFactory.CreateQueueClient(Config.ServiceBusConfig.MessageBroadcastQueueName);
					}
					catch (TimeoutException) { }
				}
			}


			if (!String.IsNullOrWhiteSpace(Config.ServiceBusConfig.AzureQueueNotificationConnectionString))
			{
				while (_notificationClient == null || _notificationClient.IsClosed)
				{
					try
					{
						var builder = new ServiceBusConnectionStringBuilder(Config.ServiceBusConfig.AzureQueueNotificationConnectionString)
						{
							OperationTimeout = TimeSpan.FromMinutes(5)
						};

						var messagingFactory = MessagingFactory.CreateFromConnectionString(builder.ToString());
						_notificationClient = messagingFactory.CreateQueueClient(Config.ServiceBusConfig.NotificaitonBroadcastQueueName);
					}
					catch (TimeoutException) { }
				}
			}

			if (!String.IsNullOrWhiteSpace(Config.ServiceBusConfig.AzureQueueShiftsConnectionString))
			{
				while (_shiftsClient == null || _shiftsClient.IsClosed)
				{
					try
					{
						var builder = new ServiceBusConnectionStringBuilder(Config.ServiceBusConfig.AzureQueueShiftsConnectionString)
						{
							OperationTimeout = TimeSpan.FromMinutes(5)
						};

						var messagingFactory = MessagingFactory.CreateFromConnectionString(builder.ToString());
						_shiftsClient = messagingFactory.CreateQueueClient(Config.ServiceBusConfig.ShiftNotificationsQueueName);
					}
					catch (TimeoutException) { }
				}
			}

			if (!String.IsNullOrWhiteSpace(Config.ServiceBusConfig.AzureQueueEmailConnectionString))
			{
				while (_distributionListClient == null || _distributionListClient.IsClosed)
				{
					try
					{
						var builder = new ServiceBusConnectionStringBuilder(Config.ServiceBusConfig.AzureQueueEmailConnectionString)
						{
							OperationTimeout = TimeSpan.FromMinutes(5)
						};

						var messagingFactory = MessagingFactory.CreateFromConnectionString(builder.ToString());
						_distributionListClient = messagingFactory.CreateQueueClient(Config.ServiceBusConfig.EmailBroadcastQueueName);
					}
					catch (TimeoutException) { }
				}
			}
		}
	}
}
