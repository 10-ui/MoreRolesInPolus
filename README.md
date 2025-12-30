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
feature/researcher ──┐
feature/anchor ──────┼→ develop ─→ main
                     │  (結合テスト) (安定版)
                        ↓         ↓
                    Snapshot   Major
```

#### ブランチの役割

- **feature/xxx**: 個別の役職開発
- **develop**: 結合テスト・統合（Snapshotリリース）
- **main**: テスト済みの安定版（メジャーリリース）

#### 開発フロー

**1. 機能開発:**
```bash
git checkout -b feature/researcher
# 開発...
git push origin feature/researcher
```

**2. 結合テスト（develop）:**
```bash
# GitHubでPR作成: feature/researcher → develop
# マージすると自動で s,Snapshot_25.12.30a リリース
```

**3. テスト・確認:**
- Among Us で Snapshot版をテスト
- 問題あれば修正して再度develop にマージ

**4. 安定版リリース（main）:**
```bash
# GitHubでPR作成: develop → main
# PRタイトルに v1.0.0 を含める
# 例: "v1.0.0: 初回リリース"
# マージすると自動で v1.0.0 リリース
```

### リリース方法

#### Snapshot（開発版）- 自動

`develop` ブランチへのマージで**自動的にリリース**：

1. feature → develop のPRをマージ
2. 自動で `s,Snapshot_25.12.30a` タグ作成
3. `MoreRolesInPolus-s,Snapshot_25.12.30a.zip` がリリース（Pre-release、Latest）

#### メジャーバージョン（正式版）- PRタイトルで判定

`main` ブランチへのマージで**PRタイトルにバージョン番号があればリリース**：

**PRタイトルの例:**

```
✅ "v1.0.0: 初回リリース" → v1.0.0 リリース
✅ "v1.2.3: 新役職追加" → v1.2.3 リリース
❌ "バグ修正" → リリースされない
```

**手順:**

1. develop → main のPRを作成
2. **PRタイトルに `v1.0.0` を含める**
3. PRをマージ
4. 自動で `v1.0.0` タグ作成 + `MoreRolesInPolus-v1.0.0.zip` リリース

### リリースの確認

[Releases ページ](https://github.com/10-ui/MoreRolesInPolus/releases) で確認できます。

---
