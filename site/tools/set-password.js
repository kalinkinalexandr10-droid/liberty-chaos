// Смена пароля админки: npm run set-password -- НовыйПароль
const { setPassword } = require('../lib/auth');

const pass = process.argv[2];
if (!pass || pass.length < 8) {
  console.error('Укажи новый пароль не короче 8 символов: npm run set-password -- НовыйПароль');
  process.exit(1);
}
setPassword(pass);
console.log('Пароль админки обновлён. Перезапусти сервер, чтобы завершить старые входы.');
