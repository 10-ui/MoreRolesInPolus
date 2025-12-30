# MoreRolesInPolus

Toa x junjun

Nebula on the Ship 用アドオン MOD。  
新しい役職・モディファイアを追加します。

---

## Imposter

### 潜望者

特製のベント（びっくり箱）を設置・使用できます。  
びっくり箱はカメラとしても機能するため、設置場所は慎重に考える必要があります。

### スポイラー

キルした相手の役職が分かり、  
その役職が **残り何人いるか** を判別できます。

※ 判別される人数はキル時点のもので、後から変化しません。

---

## Crewmate

### リサーチャー

他プレイヤーの **過去の行動** を調査できます。  
ログを辿った先に見える真実は、希望か、それとも絶望か。

---

## Neutral

### アキューサー

他役職を **一定数推測** することで勝利できます。

---

## Modifier

### スポイラー

インポスター役職「スポイラー」の  
**モディファイア版** です。

---

## 開発者向け情報

### プロジェクト構成

```
MoreRolesInPolus/
├── MoreRolesInPolus/          # ソースコード（.cs, リソースファイル）
│   ├── Scripts/              # 役職・ロジック実装
│   ├── Language/             # 言語ファイル
│   ├── Resources/            # 画像・アセット
│   └── addon.meta            # アドオン設定
├── MoreRolesInPolus.csproj   # プロジェクトファイル
├── MoreRolesInPolus.sln      # ソリューション
└── .github/workflows/        # GitHub Actions（自動リリース）
```

### 初期設定

**Among Us のパスを設定：**

1. `Directory.Build.props.user.template` を `Directory.Build.props.user` にコピー
2. `Directory.Build.props.user` を開いて、Among Us のインストールパスを修正：
   ```xml
   <AmongUs>あなたのパス\Among Us NoS_dev</AmongUs>
   ```
3. VS Studio を再起動

> `Directory.Build.props.user` は Git 管理されないので、各開発者が自分の環境に合わせて作成してください

### ビルド方法

Visual Studio で `Ctrl+Shift+B` でビルド。

- **Debug**: `Among Us NoS_dev\Addons\[Toa]MoreRolesInPolus.zip` に出力（開発用）
- **Release**: `bin\Release\MoreRolesInPolus.zip` + Among Us フォルダに出力（配布用）

> ⚠️ F5（デバッグ実行）ではなく、**ビルド（Ctrl+Shift+B）のみ**使用してください

### Git ワークフロー

```
feature/xxx ──┐
              ├─→ main ─→ release
              │   (統合)   (リリース)
```

1. **機能開発**: `feature/役職名` ブランチで開発

   ```bash
   git checkout -b feature/researcher
   ```

2. **統合**: `main` ブランチにマージ

   ```bash
   git checkout main
   git merge feature/researcher
   ```

3. **リリース**: `main` → `release` にプッシュ
   ```bash
   git checkout release
   git merge main
   git push origin release
   ```

### リリース方法

#### Snapshot（開発版）

`release` ブランチに**タグなし**でプッシュ：

```bash
git push origin release
```

→ 自動で `2025.12.30a` タグが作成され、`MoreRolesInPolus-2025.12.30a.zip` がリリースされます

#### メジャーバージョン（正式版）

タグを付けてプッシュ：

**VS Studio:**

1. **表示** → **Git リポジトリ** (`Ctrl+0, Ctrl+R`)
2. 右側のコミット履歴で最新コミットを**右クリック**
3. **新しいタグ** → `v1.0.0` と入力
4. プッシュ時に**タグをプッシュ**にチェック

**コマンドライン:**

```bash
git tag v1.0.0
git push origin release
git push origin v1.0.0
```

→ `MoreRolesInPolus-v1.0.0.zip` がリリースされます

### リリースの確認

[Releases ページ](https://github.com/10-ui/MoreRolesInPolus/releases) で確認できます。

---
