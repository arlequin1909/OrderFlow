/** Visible, on-screen error banner — never just console.error, per the requirement that API
 *  validation failures (400) must be shown to the user clearly. */
export function ErrorAlert({ title, messages }: { title: string; messages: string[] }) {
  if (messages.length === 0) return null;

  return (
    <div className="alert alert-error" role="alert">
      <strong>{title}</strong>
      {messages.length === 1 ? (
        <p>{messages[0]}</p>
      ) : (
        <ul>
          {messages.map((message, index) => (
            <li key={index}>{message}</li>
          ))}
        </ul>
      )}
    </div>
  );
}
