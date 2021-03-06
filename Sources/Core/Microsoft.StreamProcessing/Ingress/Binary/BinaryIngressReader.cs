﻿// *********************************************************************
// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License
// *********************************************************************
using System;
using System.IO;
using System.Runtime.Serialization;
using Microsoft.StreamProcessing.Internal.Collections;
using Microsoft.StreamProcessing.Serializer;

namespace Microsoft.StreamProcessing
{
    [DataContract]
    internal sealed class BinaryIngressReader<TKey, TPayload> : StreamMessageIngressBase<TKey, TPayload>
    {
        private readonly MemoryPool<TKey, TPayload> pool;
        private readonly int numMessages;
        private readonly Stream stream;
        private readonly IIngressScheduler scheduler;

        [Obsolete("Used only by serialization. Do not call directly.")]
        public BinaryIngressReader() { }

        public BinaryIngressReader(
            string identifier,
            Streamable<TKey, TPayload> streamable,
            IStreamObserver<TKey, TPayload> observer,
            int numMessages,
            Stream stream,
            IIngressScheduler scheduler,
            bool delayed)
            : base(identifier, streamable, observer)
        {
            this.pool = MemoryManager.GetMemoryPool<TKey, TPayload>();
            this.numMessages = numMessages;
            this.stream = stream;
            this.scheduler = scheduler;

            if (!delayed) this.subscription.Enable();
        }

        public override void ProduceQueryPlan(PlanNode previous)
        {
            this.Observer.ProduceQueryPlan(new IngressPlanNode(this, typeof(Empty), typeof(TPayload), false, null));
        }

        protected override IDisposable Action(IStreamObserver<TKey, TPayload> observer)
        {
            if (this.scheduler == null)
                Ingress(observer); // ingress data on current thread
            else
                this.scheduler.Schedule(() => Ingress(observer)); // ingress data on user-specified scheduler

            return Utility.EmptyDisposable;
        }

        private void Ingress(IStreamObserver<TKey, TPayload> observer)
        {
            bool done = false;
            int messages = 0;

            try
            {
                var serializer = StreamableSerializer.Create<QueuedMessage<StreamMessage<TKey, TPayload>>>(new SerializerSettings());
                while (!done)
                {
                    var message = serializer.Deserialize(this.stream);
                    if (message.Kind == MessageKind.Completed) done = true;
                    observer.OnNext(message.Message);
                    messages++;
                    if (this.numMessages != 0 && messages == this.numMessages)
                    {
                        if (message.Kind != MessageKind.Completed)
                        {
                            observer.OnCompleted();
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                observer.OnError(e);
            }
        }
    }
}
