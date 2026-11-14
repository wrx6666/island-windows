async function refresh(attempt = 0) {
  try {
    const state = await chrome.runtime.sendMessage({type:'status'});
    document.getElementById('state').textContent = state.connected ? 'Подключено к Island' : state.connecting ? 'Ожидаем приложение Island…' : 'Нет соединения с Island';
    document.getElementById('track').textContent = [state.title,state.artist].filter(Boolean).join(' · ');
    let hint;
    if (state.connected) hint = state.title ? 'Связь работает. Управление — в Island или назначенным сочетанием.' : 'Island подключён. Открой Яндекс Музыку / VK, обнови музыкальную вкладку и включи трек.';
    else if (state.failure) hint = 'Запусти Island.BrowserSetup.exe из распакованной папки → «Подключить». Затем обнови вкладку музыки. Ошибка: ' + state.failure;
    else hint = 'Запусти Island.exe из той же распакованной папки. Если переносил её — повторно нажми «Подключить» в Island.BrowserSetup.exe.';
    document.getElementById('hint').textContent = hint;
    if (state.connecting && attempt < 6) setTimeout(() => refresh(attempt + 1), 500);
  } catch {
    document.getElementById('state').textContent = 'Расширение обновлено или перезапущено';
    document.getElementById('hint').textContent = 'Закрой это окно, обнови вкладку музыки и снова открой Island Music Bridge.';
  }
}
document.getElementById('check').onclick = () => refresh();
refresh();
