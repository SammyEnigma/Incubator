﻿# Incubator

一些实验性质、学习性质的项目，包括：

## IOCPThreadPool
* 使用IOCP达到最优化使用线程的目的
* 提供简单的线程池规模收缩

## TcpPackage
* 测试tcp协议粘包及其处理

## SocketServer、SocketClient
### 对象模型
这两个项目包含了一些常见的网络基础组件，组件间包含简单的继承关系：
```
connection:
                   +-----------------+
                   |                 |
                   |  BaseConnection |
                   |                 |
                   +-+------+------+-+
                     |      |      |
                     |      |      |
      +--------------+---+  |  +---+----------------------+
      |                  |  |  |                          |
      | SocketConnection |  |  | StreamedSocketConnection |
      |                  |  |  |                          |
      +------------------+  |  +-------------+------------+
                            |                |
+------------------------+  |                |
|                        |  |                |
| SocketClientConnection +--+  +-------------+------------------+
|                        |     |                                |
+------------------------+     | StreamedSocketClientConnection |
                               |                                |
                               +--------------------------------+
```

```
listener:
           +----------------+
           |                |
           |  BaseListener  |
           |                |
           +---+--------+---+
               |        |
               |        |
               |        |
               |        |
+--------------+-+    +-+-------------+
|                |    |               |
| SocketListener |    |  RpcListener  |
|                |    |               |
+----------------+    +---------------+

```
上述类型大致分了两种，一种是普通的socket读写，另一种则是streamed读写。

两种方式各有优劣。普通的socket读写方式来驱动程序，业务协议的体现主要集中在每次recv完成后的回调处理中，然而回调能读取多少byte都不是程序能够控制的（那是属于tcp协议的东西），类似这种tcp粘包问题的处理会蔓延在整个对象模型中下层的各个地方，很丑陋，很难维护；

streamed读写则没有这种困扰（好吧其实还是有，就在那最底层的`stream.write`处），并且由于tcp本身就是一种流式协议，所以使用stream读写似乎显得更为自然。业务协议的体现现在也简单得不得了：
```csharp
    var len = await stream.write(4);
    var body = await stream.write(len);
```

这就读取到一条消息了！但是，这里总共进行了两次IO，这意味着什么？意味着更多的上下文切换，更多的资源占用和更低下的性能。

### 线程模型
.net世界的[IOCP](https://www.ibm.com/developerworks/cn/java/j-lo-iocp/index.html)天生具有[Reactor](http://ifeve.com/netty-reactor-4/)的特质，基于`APM`、`Task`又或者是[SocketAsyncEventArgs](https://docs.microsoft.com/en-us/dotnet/api/system.net.sockets.socketasynceventargs?view=netframework-4.7.2)的方式在内部微软都封装好了`IOCP`的使用。于是，参照经典的reactor线程模型，listener就是main reactor，负责侦听端口accept每个新进入socket；而每个新建立的connection则是一个又一个的sub reactor，内部`IOCP`作为事件通知方驱动了整个程序向前运行。唯一需要担心的是，当连接数量过多的时候线程池的性能问题（事件的回调都会在线程池上完成）。线程过多一定不是一件好事，上下文切换带来的开销，以及对系统资源的争抢都会降低系统整体性能。

目前使用了一个叫做`IOCompletionPortTaskScheduler`的类型来限制可用线程数量，所有的处理回调都会在这个scheduler上执行；需要注意的一点是，另外一个类型，[SocketAwaitable](https://blogs.msdn.microsoft.com/pfxteam/2011/12/15/awaiting-socket-operations/)。它存在的全部意义就在于能够在享受`await/async`编译器支持的同时还可以对socket异步操作进行“高效”等待。高效二字的体现就是并没有task对象的产生，这在巨量异步操作的情况下能够非常有效的降低gc的开销。而现在，`IOCompletionPortTaskScheduler`每一次操作都会生成一个新的task对象，这二者是仇家，是互相拆家的仇家。

### GC控制
对于streamed类型来说，流的读写是直接基于`SocketAsyncEventArgs`的buffer来做的，这也是一个有好有坏的设计。好处在于，如果saea对象的缓冲区足够大，那么`stream.read`的过程中不会有任何内存拷贝（当然如果缓冲区偏小，那么多次read操作必定需要一块额外的buffer来存放多次操作的结果）；坏处呢，也很明显，saea被设计来一次异步操作完成之后是马上可以复用的，按照这种设计，在业务说可以之前，saea是会被一直占用的！

另外关于byte的使用，目前项目中仅仅只是简单的使用了一个名为[ArrayPool](https://adamsitnik.com/Array-Pool/)的类型，让大量byte操作会用到的缓冲区全部取自这里；比较关键的一点是这些被`rent`出来的byte[]要在使用完毕后小心的`return`回去。

### 优化方向
关于服务端编程，个人认为三个方面最为重要：
* ***线程模型***
* ***GC控制***
* ***API设计（对象模型）***
  
上面的描述从这三个方面对这个项目进行了简单的描述；当中不少的问题即是将来的优化、重构方向：
* IO合并
* 需要一个不会产生task对象的scheduler（可能导致直接不走TAP模式）

## PredicateParser
目前有两个项目都会需要使用到该类型：
* 一个是CodeGenerator生成的数据访问层，我期望业务能够很方便的表达各种查询，因此需要支持表达式树的转换工作
* 另一个则是CacheRepository中仍然是出于方便的目的想让缓存项的查找能够更加方便一点（这里还有CacheRepository项目的特殊性，导致难度其实是不在一个量级的）

目前的情况是基本的表达式解析已经没有问题了，实际项目使用中发现偶尔会出现无法获取到闭包进来的变量的值，这就很难受了；目前初步猜测很多解决方案中有使用到一个效率比较低下的`exp.Compile()`方法，也许该方法隐藏了获取闭包变量值的复杂性。

