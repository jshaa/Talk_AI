// Blocks generated text that could serve messenger phishing ("엄마 나 폰 고장났어 … 돈 좀 보내줘").
// The persona must never ask for money, transfers, codes or credentials.

const RISK = [
  /계좌|송금|이체|입금|환불|대출|결제\s*(해|좀)|돈\s*(좀|을|이|보내|빌려|부쳐|필요)|급전|현금/,
  /인증\s*번호|인증\s*코드|비밀\s*번호|비번|OTP|보안\s*카드|카드\s*번호|주민\s*번호|신분증|공인\s*인증|본인\s*인증/i,
  /상품권|기프트\s*카드|구글\s*기프트|문화\s*상품권|기프티콘\s*(사|보내)/,
  /폰\s*(이\s*)?(고장|깨|액정)|휴대폰\s*(이\s*)?(고장|깨|액정)|임시\s*(폰|번호)|다른\s*번호로/,
  /(앱|어플)\s*(설치|깔아)|원격|링크\s*(눌러|클릭)/,
  /\d{2,6}-\d{2,6}-\d{2,8}/,
];

export interface SafetyVerdict {
  blocked: boolean;
}

export function checkReply(text: string): SafetyVerdict {
  return { blocked: RISK.some((re) => re.test(text)) };
}

export const BLOCKED_NOTICE = '⚠️ 돈·계좌·인증·설치 관련 내용이 감지되어 이 답장은 표시하지 않았습니다.';
