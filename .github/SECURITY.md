# Безопасность / Security

## Поддерживаемые версии

Исправления безопасности выходят только для последней опубликованной версии Blockify Launcher
(сейчас — ветка 0.2.x). Если вы на старой версии или на альфе 2024 года — сначала обновитесь.

| Версия | Поддержка |
|--------|-----------|
| 0.2.x  | да        |
| < 0.2  | нет       |

## Как сообщить об уязвимости

**Не создавайте публичный issue** — так уязвимостью успеют воспользоваться до исправления.

1. Откройте [приватный отчёт об уязвимости](https://github.com/Blockify-Launcher/Blockify-Launcher/security/advisories/new)
   (вкладка **Security → Report a vulnerability** в репозитории).
2. Опишите, что затронуто (лаунчер, сайт, релизные файлы), как воспроизвести и чем это грозит пользователю.
   Приложите версию Blockify и Windows. Не прикладывайте чужие личные данные, токены и пароли.

Мы ответим в течение 7 дней, согласуем с вами срок исправления и укажем вас в благодарностях к релизу,
если вы не против. Пожалуйста, не раскрывайте детали публично, пока исправление не выпущено.

Особенно интересны:
- выполнение кода или запись файлов вне папок игры через сборки (`.mrpack`), коды `BLK-…`, ссылки `blockify://`
  и импорт из других лаунчеров;
- утечка токенов входа Microsoft или файлов аккаунтов;
- подмена загружаемых файлов (моды, версии, Java) или релизных архивов.

Проблемы самой игры Minecraft, модов и сторонних серверов сообщайте их авторам.

## Проверка подлинности релиза

Каждый релиз публикуется с файлом `SHA256SUMS.txt` и подтверждением происхождения сборки
([GitHub Attestations](https://github.com/Blockify-Launcher/Blockify-Launcher/attestations)).
Проверить архив:

```powershell
Get-FileHash .\Blockify-portable-win-x64.zip -Algorithm SHA256
# или, с GitHub CLI:
gh attestation verify .\Blockify-portable-win-x64.zip --repo Blockify-Launcher/Blockify-Launcher
```

Скачивайте Blockify только со [страницы релизов](https://github.com/Blockify-Launcher/Blockify-Launcher/releases)
или с [официального сайта](https://blockify-launcher.github.io/Blockify-Launcher/).

---

**English:** please report vulnerabilities privately via
[GitHub Security Advisories](https://github.com/Blockify-Launcher/Blockify-Launcher/security/advisories/new),
not in public issues. We aim to respond within 7 days. Only the latest release line (0.2.x) receives security fixes.
