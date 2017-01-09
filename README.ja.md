# 認証プロキシ爆発しろ！

「認証プロキシ爆発しろ！」（英語名 MAPE: May Authentication Proxy Explode）は、
認証プロキシに対する苛立ちや呪詛の思いによって心が濁ることをソフトウェア的になんとか防ぐためのツールです。

これは認証プロキシを回避するものでは**ありません**。
認証プロキシに対応していないソフトウェアの通信を中継して、
正しく認証プロキシを通すようにするものです。

## 機能

このツールは認証プロキシに対するプロキシとして機能します。
認証プロキシが認証を要求した場合、
発信元のソフトウェアにまで戻ることなく、
このツールが認証情報を追加してリクエストを再送信します。

以下のソフトウェアが認証プロキシ環境内で動作できるようにします。

* httpまたはhttps通信を行う
* プロキシに対応している
* プロキシ認証には対応していない

プロキシとして動作しますので、元々プロキシにまったく対応していないソフトウェアには効果がありません。

Windowsデスクトップで実行した場合、
ツール実行中のみインターネットオプションのプロキシ設定を書き換えて、
このツールがユーザーの指定プロキシとなるようにします。

## 使い方

[このリポジトリのGitHub Wiki](https://github.com/ipponshimeji/MAPE/wiki/Index.ja)を参照してください。


## 実装状況

とりあえず使う必要があるので急いで作った状態で、
機能はしますが、UIとか足りていない部分があります。

現状、以下のものがあります。

* Windows向けコマンド
* Windows向けGUIアプリ

.NET Core向けにビルドすれば、コマンドはLinuxとかでも動くはずですが、
まだ試していません。
必要はあるので、そのうちやる予定。

## ライセンス

MITライセンスで提供しています。